/* miniaudio's implementation, compiled once.

   Everything above playback is switched off in CMakeLists: decoding, encoding,
   the resource manager and the node graph are all things this engine already
   has of its own, and the parts of miniaudio that do them would only be a
   second answer to the same question. */

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"
