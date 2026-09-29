cmake_minimum_required(VERSION 3.20)
project(CompositorNative LANGUAGES C)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(RENDERING "${CMAKE_CURRENT_LIST_DIR}/../../Compositor/Rendering")
add_library(compositor_native SHARED bridge.c
    ${RENDERING}/AdjustPixels.c ${RENDERING}/BrushPixels.c
    ${RENDERING}/ContentFill.c ${RENDERING}/HealPixels.c
    ${RENDERING}/LensPixels.c ${RENDERING}/LevelsPixels.c
    ${RENDERING}/NoisePixels.c ${RENDERING}/WandPixels.c)
target_include_directories(compositor_native PUBLIC "${CMAKE_CURRENT_LIST_DIR}" "${RENDERING}")
set_target_properties(compositor_native PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
if(MSVC)
    target_compile_definitions(compositor_native PRIVATE _USE_MATH_DEFINES)
elseif(NOT APPLE)
    target_link_libraries(compositor_native PRIVATE m)
endif()
