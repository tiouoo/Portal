#!/bin/bash

git submodule update --init --recursive --remote
git submodule foreach --recursive "git checkout main || git checkout -b main origin/main"
git submodule foreach --recursive "git pull origin main"
read -p "press enter to quit"
