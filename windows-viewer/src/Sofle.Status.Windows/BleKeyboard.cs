using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Sofle.Status.Windows;

internal sealed class BleKeyboard
{
    public event Action<KeyboardStatus>? StatusReceived;
    public event Action<string>? ConnectionChanged;

    public async Task RunAsync(string deviceId, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                ConnectionChanged?.Invoke("Connecting to keyboard...");
                await ConnectAsync(deviceId, cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is COMException or IOException or InvalidOperationException
                                             or UnauthorizedAccessException or FormatException or TimeoutException
                                             or ArgumentException)
            {
                AppLog.Write("Bluetooth status connection failed.", exception);
                ConnectionChanged?.Invoke($"{exception.Message} Retrying in 5s.");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ConnectAsync(string deviceId, CancellationToken cancellation)
    {
        using var setup = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        setup.CancelAfter(TimeSpan.FromSeconds(25));
        using var device = await WithTimeout(
            BluetoothLEDevice.FromIdAsync(deviceId).AsTask(setup.Token), cancellation)
            ?? throw new IOException("Paired keyboard not found. Choose keyboard again.");
        using var session = await WithTimeout(
            GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(setup.Token), cancellation)
            ?? throw new IOException("Cannot open Bluetooth session. Check Bluetooth is enabled.");
        session.MaintainConnection = true;
        var services = await WithTimeout(
            device.GetGattServicesForUuidAsync(KeyboardStatus.ServiceUuid, BluetoothCacheMode.Uncached)
                .AsTask(setup.Token), cancellation);
        Check(services.Status, "Discovering status service", services.ProtocolError);
        try
        {
            if (services.Services.Count != 1)
                throw new IOException("Status firmware service missing. Flash host-status firmware, then re-pair if needed.");
            var service = services.Services[0];
            var characteristics = await WithTimeout(
                service.GetCharacteristicsForUuidAsync(KeyboardStatus.CharacteristicUuid, BluetoothCacheMode.Uncached)
                    .AsTask(setup.Token), cancellation);
            Check(characteristics.Status, "Discovering status characteristic", characteristics.ProtocolError);
            if (characteristics.Characteristics.Count != 1)
                throw new IOException("Status characteristic missing. Check firmware version.");
            var characteristic = characteristics.Characteristics[0];
            characteristic.ProtectionLevel = GattProtectionLevel.EncryptionRequired;
            var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            uint? latestSequence = null;
            var packetGate = new object();
            var accepting = true;

            void Accept(IBuffer buffer)
            {
                using var reader = DataReader.FromBuffer(buffer);
                var bytes = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(bytes);
                var status = KeyboardStatus.Parse(bytes);
                lock (packetGate)
                {
                    if (!accepting) return;
                    // Ignore an older initial read racing a notification; handle uint32 wrap.
                    if (latestSequence is uint previous && unchecked((int)(status.Sequence - previous)) < 0)
                        return;
                    latestSequence = status.Sequence;
                    StatusReceived?.Invoke(status);
                }
            }

            void Changed(GattCharacteristic sender, GattValueChangedEventArgs args)
            {
                if (cancellation.IsCancellationRequested) return;
                try
                {
                    Accept(args.CharacteristicValue);
                }
                catch (FormatException exception)
                {
                    failure.TrySetException(exception);
                }
            }

            void Disconnected(BluetoothLEDevice sender, object args)
            {
                if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                    failure.TrySetException(new IOException("Keyboard disconnected or sleeping."));
            }

            characteristic.ValueChanged += Changed;
            device.ConnectionStatusChanged += Disconnected;
            try
            {
                var subscribed = await WithTimeout(characteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)
                    .AsTask(setup.Token), cancellation);
                Check(subscribed, "Subscribing to keyboard status");
                await ReadAsync(characteristic, Accept, setup.Token, cancellation);
                if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                    throw new IOException("Keyboard disconnected during setup.");
                setup.CancelAfter(Timeout.InfiniteTimeSpan);

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
                while (true)
                {
                    var tick = timer.WaitForNextTickAsync(cancellation).AsTask();
                    var completed = await Task.WhenAny(failure.Task, tick);
                    if (completed == failure.Task)
                        await failure.Task;
                    if (!await tick) return;
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await ReadAsync(characteristic, Accept, readTimeout.Token, cancellation);
                }
            }
            finally
            {
                lock (packetGate) accepting = false;
                characteristic.ValueChanged -= Changed;
                device.ConnectionStatusChanged -= Disconnected;
                session.MaintainConnection = false;
            }
        }
        finally
        {
            foreach (var service in services.Services) service.Dispose();
        }
    }

    private static async Task ReadAsync(GattCharacteristic characteristic, Action<IBuffer> accept,
        CancellationToken timeout, CancellationToken cancellation)
    {
        var value = await WithTimeout(
            characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(timeout), cancellation);
        Check(value.Status, "Reading keyboard status", value.ProtocolError);
        accept(value.Value);
    }

    private static async Task<T> WithTimeout<T>(Task<T> operation, CancellationToken cancellation)
    {
        try
        {
            return await operation;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("Bluetooth request timed out. Wake keyboard and check selected profile.");
        }
    }

    private static void Check(GattCommunicationStatus status, string operation, byte? protocolError = null)
    {
        if (status != GattCommunicationStatus.Success)
            throw new IOException($"{operation}: {status}{(protocolError is byte error ? $" (ATT 0x{error:X2})" : "")}. Check pairing and active Bluetooth profile.");
    }
}
