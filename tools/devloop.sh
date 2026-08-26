#!/usr/bin/env bash
set -u

make_cmd=${MAKE_CMD:?MAKE_CMD is required}
while true; do
	"$make_cmd" install run
	sleep 1
done
