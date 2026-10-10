// SPDX-License-Identifier: MIT
//
// stb_image, compiled once and exported.
//
// The texture compiler is managed code and asks this library for the one thing
// it cannot do itself: turning a PNG or a JPEG into pixels. The formats are the
// ones the compiler has always read - PNG, JPEG, TGA, BMP, HDR - and STBIDEF is
// set to the export macro by the build, so the managed side calls
// stbi_load_from_memory by its own name.

#ifdef _MSC_VER
#define _CRT_SECURE_NO_WARNINGS
#endif

#define STB_IMAGE_IMPLEMENTATION
#define STBI_ONLY_PNG
#define STBI_ONLY_JPEG
#define STBI_ONLY_TGA
#define STBI_ONLY_BMP
#define STBI_ONLY_HDR
#include "stb_image.h"
