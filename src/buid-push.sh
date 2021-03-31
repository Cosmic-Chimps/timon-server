#!/usr/bin/env bash

docker build --network host . -t jjchiw/timon-server:$(git rev-parse --short HEAD)
docker push jjchiw/timon-server:$(git rev-parse --short HEAD)
git rev-parse --short HEAD
