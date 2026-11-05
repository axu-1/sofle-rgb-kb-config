if(CONFIG_ZMK_HOST_STATUS)
  target_sources(app PRIVATE ${CMAKE_CURRENT_LIST_DIR}/host_status.c)
endif()
