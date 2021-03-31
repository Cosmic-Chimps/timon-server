#!/bin/sh

cd migrations
dotnet TimonMigrations.dll

cd ..
dotnet TimonServer.dll
