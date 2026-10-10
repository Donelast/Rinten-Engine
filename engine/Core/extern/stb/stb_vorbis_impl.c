/* stb_vorbis's implementation, compiled once.

   Only the pull-from-memory decoder is used: an ogg is decoded in one go when
   the sound is loaded, so none of the file APIs are wanted here. */

#define STB_VORBIS_NO_STDIO
#define STB_VORBIS_NO_PUSHDATA_API

#include "stb_vorbis.c"
