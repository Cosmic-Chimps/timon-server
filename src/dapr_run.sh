#dapr run --app-id timon-server --app-port 5011 dotnet /bin/Debug/net5.0/TimonServer.dll


daprd --app-id "timon-server" --app-port "5004" --components-path "./components" --dapr-grpc-port "50004" --dapr-http-port "3502" "--enable-metrics=false" --placement-address "localhost:50005"
