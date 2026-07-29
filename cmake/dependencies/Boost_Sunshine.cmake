#
# Loads the boost library giving the priority to the system package first, with a fallback to FetchContent.
#
include_guard(GLOBAL)

set(BOOST_VERSION "1.89.0")
set(BOOST_COMPONENTS
        filesystem
        locale
        log
        program_options
        system
)
# system is not used by Sunshine, but by Simple-Web-Server, added here for convenience

# algorithm, preprocessor, scope, and uuid are not used by Sunshine, but by libdisplaydevice, added here for convenience
if(WIN32)
    list(APPEND BOOST_COMPONENTS
            algorithm
            preprocessor
            scope
            uuid
    )
endif()

if(BOOST_USE_STATIC)
    set(Boost_USE_STATIC_LIBS ON)  # cmake-lint: disable=C0103
endif()

if (CMAKE_VERSION VERSION_GREATER_EQUAL "3.30")
    cmake_policy(SET CMP0167 NEW)  # Get BoostConfig.cmake from upstream
endif()
if(WIN32)
    if(NOT DEFINED LIGASE_BOOST_ARCHIVE OR LIGASE_BOOST_ARCHIVE STREQUAL "")
        message(FATAL_ERROR "LIGASE_BOOST_ARCHIVE is required for offline Windows builds")
    endif()
    get_filename_component(LIGASE_BOOST_ARCHIVE_ABSOLUTE
            "${LIGASE_BOOST_ARCHIVE}" ABSOLUTE)
    if(NOT LIGASE_BOOST_ARCHIVE_ABSOLUTE MATCHES "^[Dd]:[/\\\\]")
        message(FATAL_ERROR "LIGASE_BOOST_ARCHIVE must be on the D drive")
    endif()
    if(NOT EXISTS "${LIGASE_BOOST_ARCHIVE_ABSOLUTE}" OR
            IS_DIRECTORY "${LIGASE_BOOST_ARCHIVE_ABSOLUTE}")
        message(FATAL_ERROR "LIGASE_BOOST_ARCHIVE is unavailable")
    endif()
    file(SIZE "${LIGASE_BOOST_ARCHIVE_ABSOLUTE}" LIGASE_BOOST_ARCHIVE_SIZE)
    if(NOT LIGASE_BOOST_ARCHIVE_SIZE EQUAL 102078704)
        message(FATAL_ERROR "LIGASE_BOOST_ARCHIVE size mismatch")
    endif()
    file(SHA256 "${LIGASE_BOOST_ARCHIVE_ABSOLUTE}"
            LIGASE_BOOST_ARCHIVE_SHA256)
    if(NOT LIGASE_BOOST_ARCHIVE_SHA256 STREQUAL
            "67acec02d0d118b5de9eb441f5fb707b3a1cdd884be00ca24b9a73c995511f74")
        message(FATAL_ERROR "LIGASE_BOOST_ARCHIVE hash mismatch")
    endif()
    set(Boost_FOUND FALSE)
else()
    find_package(Boost CONFIG ${BOOST_VERSION} EXACT COMPONENTS ${BOOST_COMPONENTS})
endif()
if(NOT Boost_FOUND)
    message(STATUS "Boost v${BOOST_VERSION} package not found in the system. Falling back to FetchContent.")
    include(FetchContent)

    if (CMAKE_VERSION VERSION_GREATER_EQUAL "3.24.0")
        cmake_policy(SET CMP0135 NEW)  # Avoid warning about DOWNLOAD_EXTRACT_TIMESTAMP in CMake 3.24
    endif()
    if (CMAKE_VERSION VERSION_GREATER_EQUAL "3.31.0")
        cmake_policy(SET CMP0174 NEW)  # Handle empty variables
    endif()

    # more components required for compiling boost targets
    list(APPEND BOOST_COMPONENTS
            asio
            crc
            format
            process
            property_tree)

    set(BOOST_ENABLE_CMAKE ON)

    # Limit boost to the required libraries only
    set(BOOST_INCLUDE_LIBRARIES ${BOOST_COMPONENTS})
    set(BOOST_URL "https://github.com/boostorg/boost/releases/download/boost-${BOOST_VERSION}/boost-${BOOST_VERSION}-cmake.tar.xz")  # cmake-lint: disable=C0301
    set(BOOST_HASH "SHA256=67acec02d0d118b5de9eb441f5fb707b3a1cdd884be00ca24b9a73c995511f74")
    if(WIN32)
        set(BOOST_URL "${LIGASE_BOOST_ARCHIVE_ABSOLUTE}")
    endif()

    if(CMAKE_VERSION VERSION_LESS "3.24.0")
        FetchContent_Declare(
                Boost
                URL ${BOOST_URL}
                URL_HASH ${BOOST_HASH}
        )
    elseif(APPLE AND CMAKE_VERSION VERSION_GREATER_EQUAL "3.25.0")
        # add SYSTEM to FetchContent_Declare, this fails on debian bookworm
        FetchContent_Declare(
                Boost
                URL ${BOOST_URL}
                URL_HASH ${BOOST_HASH}
                SYSTEM  # requires CMake 3.25+
                OVERRIDE_FIND_PACKAGE  # requires CMake 3.24+, but we have a macro to handle it for other versions
        )
    elseif(CMAKE_VERSION VERSION_GREATER_EQUAL "3.24.0")
        FetchContent_Declare(
                Boost
                URL ${BOOST_URL}
                URL_HASH ${BOOST_HASH}
                OVERRIDE_FIND_PACKAGE  # requires CMake 3.24+, but we have a macro to handle it for other versions
        )
    endif()

    FetchContent_MakeAvailable(Boost)
    set(FETCH_CONTENT_BOOST_USED TRUE)

    set(Boost_FOUND TRUE)  # cmake-lint: disable=C0103
    set(Boost_INCLUDE_DIRS  # cmake-lint: disable=C0103
            "$<BUILD_INTERFACE:${Boost_SOURCE_DIR}/libs/headers/include>")

    if(WIN32)
        # Windows build is failing to create .h file in this directory
        file(MAKE_DIRECTORY ${Boost_BINARY_DIR}/libs/log/src/windows)
    endif()

    set(Boost_LIBRARIES "")  # cmake-lint: disable=C0103
    foreach(component ${BOOST_COMPONENTS})
        list(APPEND Boost_LIBRARIES "Boost::${component}")
    endforeach()
endif()

message(STATUS "Boost include dirs: ${Boost_INCLUDE_DIRS}")
message(STATUS "Boost libraries: ${Boost_LIBRARIES}")
