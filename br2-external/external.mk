# Project patches are selected by BR2_GLOBAL_PATCH_DIR in each defconfig.

ifeq ($(BR2_PACKAGE_TOR_CLIENT_ONLY),y)
TOR_CONF_OPTS += --disable-module-relay --disable-module-dirauth
endif
