using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using static Lidgren.Network.NetNativeSocket;

namespace Lidgren.Network
{
	internal static unsafe class NetFastSocket
	{
		// .NET's built-in socket datagram APIs allocate constantly due to poor design.
		// These don't thanks to usage of a custom NetSocketAddress.

		public static int SendTo(Socket socket, ReadOnlySpan<byte> buffer, SocketFlags socketFlags, IPEndPoint endPoint)
		{
			var socketAddress = (NetSocketAddress) endPoint;

			return SendTo(socket, buffer, socketFlags, socketAddress);
		}

		public static int SendTo(
			Socket socket,
			ReadOnlySpan<byte> buffer,
			SocketFlags socketFlags,
			in NetSocketAddress socketAddress)
		{
			if (socketFlags != SocketFlags.None)
				throw new ArgumentException("Non-none socket flags not currently supported.");

			int ret;

			if (IsFreeBsd || IsMacOS)
			{
				sockaddr_bsd* address;
				int addressLen;

				if (socketAddress.Family == NetIpAddressFamily.V6)
				{
					sockaddr_in6_bsd address6 = default;

					ref var refAddress6 = ref Unsafe.AsRef(socketAddress.V6);

					address6.sin6_len = (byte) sizeof(sockaddr_in6_bsd);
					address6.sin6_port = htons(refAddress6.Port);
					address6.sin6_family = AF_INET6;
					address6.sin6_scope_id = refAddress6.ScopeId;
					address6.sin6_addr = Unsafe.As<NetIpv6Address, in6_addr>(ref refAddress6.Address);

					address = (sockaddr_bsd*) (&address6);
					addressLen = sizeof(sockaddr_in6_bsd);
				}
				else
				{
					Debug.Assert(socketAddress.Family == NetIpAddressFamily.V4);

					ref var refAddress4 = ref Unsafe.AsRef(socketAddress.V4);

					sockaddr_in_bsd address4 = default;
					address4.sin_len = (byte) sizeof(sockaddr_in_bsd);
					address4.sin_port = htons(refAddress4.Port);
					address4.sin_family = AF_INET;
					address4.sin_addr = Unsafe.As<NetIpv4Address, in_addr>(ref refAddress4.Address);

					address = (sockaddr_bsd*) (&address4);
					addressLen = sizeof(sockaddr_in_bsd);
				}

				fixed (byte* bufPtr = buffer)
				{
					ret = (int) sendto_bsd(
						(int) socket.Handle,
						bufPtr,
						(IntPtr) buffer.Length,
						(int) socketFlags,
						address,
						(uint) addressLen);
				}
			}
			else
			{
				sockaddr* address;
				int addressLen;

				if (socketAddress.Family == NetIpAddressFamily.V6)
				{
					sockaddr_in6 address6 = default;

					ref var refAddress6 = ref Unsafe.AsRef(socketAddress.V6);

					address6.sin6_port = htons(refAddress6.Port);
					address6.sin6_family = AF_INET6;
					address6.sin6_scope_id = refAddress6.ScopeId;
					address6.sin6_addr = Unsafe.As<NetIpv6Address, in6_addr>(ref refAddress6.Address);

					address = (sockaddr*) (&address6);
					addressLen = sizeof(sockaddr_in6);
				}
				else
				{
					Debug.Assert(socketAddress.Family == NetIpAddressFamily.V4);

					ref var refAddress4 = ref Unsafe.AsRef(socketAddress.V4);

					sockaddr_in address4 = default;
					address4.sin_port = htons(refAddress4.Port);
					address4.sin_family = AF_INET;
					address4.sin_addr = Unsafe.As<NetIpv4Address, in_addr>(ref refAddress4.Address);

					address = (sockaddr*) (&address4);
					addressLen = sizeof(sockaddr_in);
				}

				fixed (byte* bufPtr = buffer)
				{
					if (IsWindows)
					{
						ret = sendto_win32(
							socket.Handle,
							bufPtr,
							buffer.Length,
							(int) socketFlags,
							address,
							addressLen);
					}
					else
					{
						ret = (int) sendto_linux(
							(int) socket.Handle,
							bufPtr,
							(IntPtr) buffer.Length,
							(int) socketFlags,
							address,
							(uint) addressLen);
					}
				}
			}


			if (ret != -1)
				return ret;

			// Error occured
			if (IsWindows)
			{
				var errCode = WSAGetLastError();
				throw new SocketException(errCode);
			}
			else
			{
				// This apparently works??
				throw new SocketException();
			}
		}

		public static int ReceiveFrom(
			Socket socket,
			Span<byte> buffer,
			SocketFlags socketFlags,
			out NetSocketAddress socketAddress)
		{
			if (socketFlags != SocketFlags.None)
				throw new ArgumentException("Non-none socket flags not currently supported.");

			int ret;

			if (IsFreeBsd || IsMacOS)
			{
				sockaddr_in6_bsd address6;
				address6.sin6_len = (byte) sizeof(sockaddr_in6_bsd);

				fixed (byte* bufPtr = buffer)
				{
					var len = (uint) sizeof(sockaddr_in6_bsd);
					ret = (int) recvfrom_bsd(
						(int) socket.Handle,
						bufPtr,
						(IntPtr) buffer.Length,
						(int) socketFlags,
						(sockaddr_bsd*) (&address6),
						&len);
				}

				if (ret != -1)
				{
					if (address6.sin6_family == AF_INET6)
					{
						// IPv6
						socketAddress = new NetSocketAddressV6
						{
							Address = Unsafe.As<in6_addr, NetIpv6Address>(ref address6.sin6_addr),
							Port = ntohs(address6.sin6_port),
							ScopeId = address6.sin6_scope_id
						};
					}
					else if (address6.sin6_family == AF_INET)
					{
						// IPv4
						ref var address4 = ref Unsafe.As<sockaddr_in6_bsd, sockaddr_in_bsd>(ref address6);
						socketAddress = new NetSocketAddressV4
						{
							Address = Unsafe.As<in_addr, NetIpv4Address>(ref address4.sin_addr),
							Port = ntohs(address4.sin_port),
						};
					}
					else
					{
						throw new SocketException((int) SocketError.ProtocolFamilyNotSupported);
					}

					return ret;
				}
			}
			else
			{
				sockaddr_in6 address6;

				fixed (byte* bufPtr = buffer)
				{
					if (IsWindows)
					{
						var len = sizeof(sockaddr_in6);
						ret = recvfrom_win32(
							socket.Handle,
							bufPtr,
							buffer.Length,
							(int) socketFlags,
							(sockaddr*) (&address6),
							&len);
					}
					else
					{
						var len = (uint) sizeof(sockaddr_in6);
						ret = (int) recvfrom_linux(
							(int) socket.Handle,
							bufPtr,
							(IntPtr) buffer.Length,
							(int) socketFlags,
							(sockaddr*) (&address6),
							&len);
					}
				}

				if (ret != -1)
				{
					if (address6.sin6_family == AF_INET6)
					{
						// IPv6
						socketAddress = new NetSocketAddressV6
						{
							Address = Unsafe.As<in6_addr, NetIpv6Address>(ref address6.sin6_addr),
							Port = ntohs(address6.sin6_port),
							ScopeId = address6.sin6_scope_id
						};
					}
					else if (address6.sin6_family == AF_INET)
					{
						// IPv4
						ref var address4 = ref Unsafe.As<sockaddr_in6, sockaddr_in>(ref address6);
						socketAddress = new NetSocketAddressV4
						{
							Address = Unsafe.As<in_addr, NetIpv4Address>(ref address4.sin_addr),
							Port = ntohs(address4.sin_port),
						};
					}
					else
					{
						throw new SocketException((int) SocketError.ProtocolFamilyNotSupported);
					}

					return ret;
				}
			}

			// Errors occured
			if (IsWindows)
			{
				var errCode = WSAGetLastError();
				throw new SocketException(errCode);
			}
			else
			{
				// This apparently works??
				throw new SocketException();
			}
		}
	}
}
