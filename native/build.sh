#!/usr/bin/env sh
set -e
cd "$(dirname "$0")"
mkdir -p bin
case "$(uname -s)" in
  Darwin)               gcc -std=c99 -O2 -Wall -Wextra -dynamiclib -o bin/libdeliverynative.dylib deliveryfee.c ;;
  MINGW*|MSYS*|CYGWIN*) gcc -std=c99 -O2 -Wall -Wextra -shared -static -o bin/deliverynative.dll deliveryfee.c ;;
  *)                    gcc -std=c99 -O2 -Wall -Wextra -shared -fPIC -o bin/libdeliverynative.so deliveryfee.c ;;
esac
ls bin
