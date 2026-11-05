# Project patches are selected by BR2_GLOBAL_PATCH_DIR in each defconfig.

ifeq ($(BR2_PACKAGE_TOR_CLIENT_ONLY),y)
TOR_CONF_OPTS += --disable-module-relay --disable-module-dirauth
endif

EMBEDDED_TOR_CFLAGS = $(TARGET_CFLAGS)
ifeq ($(BR2_PACKAGE_TOR_OPTIMIZE_O2),y)
EMBEDDED_TOR_CFLAGS = $(filter-out -Os,$(TARGET_CFLAGS)) -O2
endif
ifeq ($(BR2_PACKAGE_TOR_GC_SECTIONS),y)
EMBEDDED_TOR_CFLAGS += -ffunction-sections -fdata-sections
TOR_CONF_ENV += LDFLAGS="$(TARGET_LDFLAGS) -Wl,--gc-sections"
# Upstream adds -rdynamic only for backtrace symbolization. This musl
# executable has no binary plugin ABI; retain all other upstream flags.
define EMBEDDED_TOR_REMOVE_DIAGNOSTIC_EXPORTS
	$(SED) 's/ -rdynamic//g' $(@D)/Makefile
endef
TOR_POST_CONFIGURE_HOOKS += EMBEDDED_TOR_REMOVE_DIAGNOSTIC_EXPORTS
endif
TOR_CONF_ENV += CFLAGS="$(EMBEDDED_TOR_CFLAGS)"
