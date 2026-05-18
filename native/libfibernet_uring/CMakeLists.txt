cmake_minimum_required(VERSION 3.20)
project(fibernet_uring C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release)
endif()

# ── Find liburing ────────────────────────────────────────────────────────────
# Prefer pkg-config; fall back to find_library + find_path.
find_package(PkgConfig QUIET)
if(PKG_CONFIG_FOUND)
  pkg_check_modules(URING QUIET liburing)
endif()

if(NOT URING_FOUND)
  find_library(URING_LIBRARIES uring REQUIRED
    DOC "liburing shared library (install liburing-dev / liburing-devel)")
  find_path(URING_INCLUDE_DIRS liburing.h REQUIRED
    DOC "Directory containing liburing.h")
  set(URING_FOUND TRUE)
endif()

message(STATUS "liburing include: ${URING_INCLUDE_DIRS}")
message(STATUS "liburing lib:     ${URING_LIBRARIES}")

# ── Shared library ───────────────────────────────────────────────────────────
add_library(fibernet_uring SHARED
    fibernet_uring.c
)

target_compile_options(fibernet_uring PRIVATE
    -Wall -Wextra -Wpedantic -O2 -fvisibility=hidden
)

# Mark exported symbols visible explicitly via function prototypes
target_compile_definitions(fibernet_uring PRIVATE
    _GNU_SOURCE
)

target_include_directories(fibernet_uring PRIVATE ${URING_INCLUDE_DIRS})
target_link_libraries(fibernet_uring PRIVATE ${URING_LIBRARIES})

set_target_properties(fibernet_uring PROPERTIES
    SOVERSION 1
    VERSION   1.0.0
    C_VISIBILITY_PRESET default   # exported C functions visible by default
    PUBLIC_HEADER fibernet_uring.h
)

# ── Install ──────────────────────────────────────────────────────────────────
include(GNUInstallDirs)
install(TARGETS fibernet_uring
    LIBRARY       DESTINATION ${CMAKE_INSTALL_LIBDIR}
    PUBLIC_HEADER DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}
)
