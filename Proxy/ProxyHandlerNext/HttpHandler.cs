﻿using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Proxy.Headers;
using Proxy.Tunnels;

namespace Proxy.ProxyHandlerNext
{
    public class HttpHandler : IHandler
    {
        private const int BufferSize = 8192;

        public async Task<HandlerResult> Run(Context context)
        {
            if (IsNewHostRequired(context))
            {
                return HandlerResult.NewHostRequired;
            }

            using (OneWayTunnel(context.HostStream, context.ClientStream))
            {
                var buffer = new byte[BufferSize];
                int bytesRead;

                do
                {
                    context.Header = await GetHeader(context.Header, context.ClientStream);

                    if (context.Header == null)
                    {
                        return HandlerResult.Terminated;
                    }

                    if (IsNewHostRequired(context))
                    {
                        return HandlerResult.NewHostRequired;
                    }

                    bytesRead = await ForwardHeader(context.Header, context.HostStream);

                    if (HasBody(context.Header))
                    {
                        bytesRead = await ForwardBody(context.ClientStream, context.HostStream, context.Header.ContentLength, buffer);
                    }

                    context.Header = null;
                } while (bytesRead > 0);
            }

            return HandlerResult.Terminated;
        }

        private static bool IsNewHostRequired(Context context)
        {
            return context.CurrentHostAddress == null || !Equals(context.Header.Host, context.CurrentHostAddress);
        }

        private static TcpOneWayTunnel OneWayTunnel(NetworkStream source, NetworkStream destination)
        {
            var tunnel = new TcpOneWayTunnel();
            tunnel.Run(destination, source).GetAwaiter();
            return tunnel;
        }

        private static async Task<HttpHeader> GetHeader(HttpHeader header, Stream stream)
        {
            return header ?? await new HttpHeaderStream().GetHeader(stream, CancellationToken.None);
        }

        private static async Task<int> ForwardHeader(HttpHeader httpHeader, Stream host)
        {
            await host.WriteAsync(httpHeader.Array, 0, httpHeader.Array.Length);
            return httpHeader.Array.Length;
        }

        private static bool HasBody(HttpHeader header)
        {
            return header.ContentLength > 0;
        }

        private static async Task<int> ForwardBody(Stream client, Stream host, long contentLength, byte[] buffer)
        {
            int bytesRead;

            do
            {
                bytesRead = await client.ReadAsync(buffer, 0, contentLength > BufferSize ? BufferSize : (int) contentLength);
                await host.WriteAsync(buffer, 0, bytesRead);
                contentLength -= bytesRead;
            } while (bytesRead > 0 && contentLength > 0);

            return bytesRead;
        }
    }
}