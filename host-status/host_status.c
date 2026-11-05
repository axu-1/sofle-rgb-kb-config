#include <errno.h>
#include <stdint.h>
#include <string.h>
#include <zephyr/bluetooth/conn.h>
#include <zephyr/bluetooth/gatt.h>
#include <zephyr/bluetooth/uuid.h>
#include <zephyr/kernel.h>
#include <zephyr/logging/log.h>
#include <zephyr/sys/atomic.h>
#include <zephyr/sys/byteorder.h>
#include <zmk/ble.h>
#include <zmk/endpoints.h>
#include <zmk/event_manager.h>
#include <zmk/events/battery_state_changed.h>
#include <zmk/events/ble_active_profile_changed.h>
#include <zmk/events/endpoint_changed.h>
#include <zmk/events/hid_indicators_changed.h>
#include <zmk/events/layer_state_changed.h>
#include <zmk/events/usb_conn_state_changed.h>
#include <zmk/hid_indicators.h>
#include <zmk/keymap.h>
#include <zmk/usb.h>

LOG_MODULE_REGISTER(sofle_host_status, CONFIG_ZMK_LOG_LEVEL);

#define STATUS_SIZE 20
#define UNKNOWN_LEVEL UINT8_MAX
#define SERVICE_UUID BT_UUID_128_ENCODE(0x8c6b7a10, 0x5f2d, 0x4a7c, 0x9b31, 0x6e8d0f2c4100)
#define STATUS_UUID BT_UUID_128_ENCODE(0x8c6b7a10, 0x5f2d, 0x4a7c, 0x9b31, 0x6e8d0f2c4101)

static struct bt_uuid_128 service_uuid = BT_UUID_INIT_128(SERVICE_UUID);
static struct bt_uuid_128 status_uuid = BT_UUID_INIT_128(STATUS_UUID);
static struct k_spinlock battery_lock;
static uint8_t central_battery = UNKNOWN_LEVEL;
static uint8_t peripheral_battery = UNKNOWN_LEVEL;
static int64_t peripheral_sample_time = -1;
static atomic_t sequence;

static void find_peripheral(struct bt_conn *conn, void *data) {
    struct bt_conn_info info;
    if (bt_conn_get_info(conn, &info) == 0 && info.type == BT_CONN_TYPE_LE &&
        info.role == BT_CONN_ROLE_CENTRAL && info.state == BT_CONN_STATE_CONNECTED) {
        *(bool *)data = true;
    }
}

static bool peripheral_is_connected(void) {
    bool connected = false;
    bt_conn_foreach(BT_CONN_TYPE_LE, find_peripheral, &connected);
    return connected;
}

static void snapshot(uint8_t data[STATUS_SIZE]) {
    memset(data, 0, STATUS_SIZE);
    data[0] = 1;
    data[1] = zmk_keymap_highest_layer_active();
    k_spinlock_key_t key = k_spin_lock(&battery_lock);
    data[2] = central_battery;
    data[3] = peripheral_battery;
    int64_t sampled_at = peripheral_sample_time;
    k_spin_unlock(&battery_lock, key);
    data[4] = zmk_ble_active_profile_index();
    struct zmk_endpoint_instance endpoint = zmk_endpoints_selected();
    data[5] = endpoint.transport == ZMK_TRANSPORT_USB ? 1 : 2;
    if (zmk_ble_active_profile_is_connected()) {
        data[6] |= BIT(0);
    }
#if IS_ENABLED(CONFIG_USB_DEVICE_STACK)
    if (zmk_usb_is_powered()) {
        data[6] |= BIT(1);
    }
#endif
    if (peripheral_is_connected()) {
        data[6] |= BIT(2);
    }
    data[6] |= BIT(3);
    data[7] = zmk_hid_indicators_get_current_profile();
    sys_put_le32(zmk_keymap_layer_state(), &data[8]);
    sys_put_le32((uint32_t)atomic_get(&sequence), &data[12]);
    uint32_t age = sampled_at < 0 ? UINT32_MAX : (uint32_t)((k_uptime_get() - sampled_at) / 1000);
    sys_put_le32(age, &data[16]);
}

static ssize_t read_status(struct bt_conn *conn, const struct bt_gatt_attr *attr, void *buf,
                           uint16_t len, uint16_t offset) {
    struct bt_conn *active = zmk_ble_active_profile_conn();
    bool authorized = active != NULL && active == conn;
    if (active != NULL) {
        bt_conn_unref(active);
    }
    if (!authorized) {
        return BT_GATT_ERR(BT_ATT_ERR_AUTHORIZATION);
    }
    uint8_t data[STATUS_SIZE];
    snapshot(data);
    return bt_gatt_attr_read(conn, attr, buf, len, offset, data, sizeof(data));
}

BT_GATT_SERVICE_DEFINE(host_status_service,
                      BT_GATT_PRIMARY_SERVICE(&service_uuid),
                      BT_GATT_CHARACTERISTIC(&status_uuid.uuid,
                                             BT_GATT_CHRC_READ | BT_GATT_CHRC_NOTIFY,
                                             BT_GATT_PERM_READ_ENCRYPT, read_status, NULL, NULL),
                      BT_GATT_CCC(NULL, BT_GATT_PERM_READ_ENCRYPT | BT_GATT_PERM_WRITE_ENCRYPT));

static void publish_status(struct k_work *work) {
    atomic_inc(&sequence);
    if (!zmk_ble_active_profile_is_connected()) {
        return;
    }
    struct bt_conn *conn = zmk_ble_active_profile_conn();
    if (conn == NULL) {
        return;
    }
    if (bt_gatt_is_subscribed(conn, &host_status_service.attrs[2], BT_GATT_CCC_NOTIFY)) {
        uint8_t data[STATUS_SIZE];
        snapshot(data);
        int err = bt_gatt_notify(conn, &host_status_service.attrs[2], data, sizeof(data));
        if (err != 0) {
            LOG_WRN("Status notification failed: %d", err);
        }
    }
    bt_conn_unref(conn);
}

K_WORK_DELAYABLE_DEFINE(status_work, publish_status);

static void schedule_status(void) {
    k_work_reschedule(&status_work, K_MSEC(10));
}

static int status_event(const zmk_event_t *event) {
    const struct zmk_battery_state_changed *battery = as_zmk_battery_state_changed(event);
    const struct zmk_peripheral_battery_state_changed *peripheral =
        as_zmk_peripheral_battery_state_changed(event);
    bool valid_peripheral = peripheral != NULL && peripheral_is_connected();
    if (battery != NULL || (peripheral != NULL && peripheral->source == 0)) {
        k_spinlock_key_t key = k_spin_lock(&battery_lock);
        if (battery != NULL) {
            central_battery = battery->state_of_charge;
        } else {
            // ZMK v0.3 emits a synthetic 0% battery event on disconnection.
            peripheral_battery = valid_peripheral ? peripheral->state_of_charge : UNKNOWN_LEVEL;
            peripheral_sample_time = valid_peripheral ? k_uptime_get() : -1;
        }
        k_spin_unlock(&battery_lock, key);
    }
    schedule_status();
    return ZMK_EV_EVENT_BUBBLE;
}

static void reset_peripheral_sample(struct bt_conn *conn) {
    struct bt_conn_info info;
    if (bt_conn_get_info(conn, &info) == 0 && info.type == BT_CONN_TYPE_LE &&
        info.role == BT_CONN_ROLE_CENTRAL) {
        k_spinlock_key_t key = k_spin_lock(&battery_lock);
        peripheral_battery = UNKNOWN_LEVEL;
        peripheral_sample_time = -1;
        k_spin_unlock(&battery_lock, key);
    }
}

static void connected(struct bt_conn *conn, uint8_t err) {
    reset_peripheral_sample(conn);
    schedule_status();
}

static void disconnected(struct bt_conn *conn, uint8_t reason) {
    reset_peripheral_sample(conn);
    schedule_status();
}

static void security_changed(struct bt_conn *conn, bt_security_t level, enum bt_security_err err) {
    schedule_status();
}

BT_CONN_CB_DEFINE(host_status_connections) = {
    .connected = connected,
    .disconnected = disconnected,
    .security_changed = security_changed,
};

ZMK_LISTENER(host_status, status_event);
ZMK_SUBSCRIPTION(host_status, zmk_layer_state_changed);
ZMK_SUBSCRIPTION(host_status, zmk_battery_state_changed);
ZMK_SUBSCRIPTION(host_status, zmk_ble_active_profile_changed);
ZMK_SUBSCRIPTION(host_status, zmk_endpoint_changed);
ZMK_SUBSCRIPTION(host_status, zmk_hid_indicators_changed);
#if IS_ENABLED(CONFIG_ZMK_SPLIT_BLE_CENTRAL_BATTERY_LEVEL_FETCHING)
ZMK_SUBSCRIPTION(host_status, zmk_peripheral_battery_state_changed);
#endif
#if IS_ENABLED(CONFIG_USB_DEVICE_STACK)
ZMK_SUBSCRIPTION(host_status, zmk_usb_conn_state_changed);
#endif
