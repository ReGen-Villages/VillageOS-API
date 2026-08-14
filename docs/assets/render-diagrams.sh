#!/usr/bin/env bash
#
# Renders a diagram source to the PNG the documents embed and the SVG the web and the wiki use.
#
# The PNG is stamped with a physical size the renderer does not write. A word processor that finds no
# size in the file guesses one, and the diagram lands as a thumbnail. Declaring 96 pixels per inch
# makes a diagram rendered at three times scale describe itself as roughly 600 mm wide — wider than
# any page, so the importer scales it down to the text column. Declaring the exact target width does
# not work: importers apply their own scaling on top of it.

set -euo pipefail
cd "$(dirname "$0")"

if [ "$#" -eq 0 ]; then set -- *.mmd; fi

for source in "$@"; do
  diagram="${source%.mmd}"

  npx -y @mermaid-js/mermaid-cli@11 -i "$diagram.mmd" -o "$diagram.png" -c mermaid-config.json -b white -s 3
  npx -y @mermaid-js/mermaid-cli@11 -i "$diagram.mmd" -o "$diagram.svg" -c mermaid-config.json -b white

  python3 - "$diagram.png" <<'DECLARE_PHYSICAL_SIZE'
import struct
import sys
import zlib

PIXELS_PER_METRE = 3780
END_OF_HEADER_CHUNK = 33

path = sys.argv[1]
image = open(path, 'rb').read()

if image[END_OF_HEADER_CHUNK + 4:END_OF_HEADER_CHUNK + 8] == b'pHYs':
    raise SystemExit(f'{path} already declares a physical size, so this step can go.')

declaration = struct.pack('>IIB', PIXELS_PER_METRE, PIXELS_PER_METRE, 1)
chunk = (struct.pack('>I', len(declaration))
         + b'pHYs' + declaration
         + struct.pack('>I', zlib.crc32(b'pHYs' + declaration)))

open(path, 'wb').write(
    image[:END_OF_HEADER_CHUNK] + chunk + image[END_OF_HEADER_CHUNK:])
DECLARE_PHYSICAL_SIZE
done
