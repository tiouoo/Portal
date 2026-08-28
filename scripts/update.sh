#!/bin/bash
set -e

repository_root="$(cd "$(dirname "$0")/.." && pwd)"
git -C "$repository_root" submodule update --init --recursive --remote

read -p "press enter to quit"
