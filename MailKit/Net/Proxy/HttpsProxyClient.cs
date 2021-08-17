﻿//
// HttpsProxyClient.cs
//
// Author: Jeffrey Stedfast <jestedfa@microsoft.com>
//
// Copyright (c) 2013-2021 .NET Foundation and Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Buffers;
using System.Threading;
using System.Net.Security;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using MailKit.Security;

using SslStream = MailKit.Net.SslStream;
using NetworkStream = MailKit.Net.NetworkStream;

namespace MailKit.Net.Proxy {
	/// <summary>
	/// An HTTPS proxy client.
	/// </summary>
	/// <remarkas>
	/// An HTTPS proxy client.
	/// </remarkas>
	public class HttpsProxyClient : ProxyClient
	{
#if NET48 || NET5_0_OR_GREATER
		const SslProtocols DefaultSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
#else
		const SslProtocols DefaultSslProtocols = SslProtocols.Tls12 | (SslProtocols) 12288;
#endif
		const int BufferSize = 4096;

		/// <summary>
		/// Initializes a new instance of the <see cref="T:MailKit.Net.Proxy.HttpsProxyClient"/> class.
		/// </summary>
		/// <remarks>
		/// Initializes a new instance of the <see cref="T:MailKit.Net.Proxy.HttpsProxyClient"/> class.
		/// </remarks>
		/// <param name="host">The host name of the proxy server.</param>
		/// <param name="port">The proxy server port.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="host"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>1</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>The <paramref name="host"/> is a zero-length string.</para>
		/// <para>-or-</para>
		/// <para>The length of <paramref name="host"/> is greater than 255 characters.</para>
		/// </exception>
		public HttpsProxyClient (string host, int port) : base (host, port)
		{
			SslProtocols = DefaultSslProtocols;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="T:MailKit.Net.Proxy.HttpsProxyClient"/> class.
		/// </summary>
		/// <remarks>
		/// Initializes a new instance of the <see cref="T:MailKit.Net.Proxy.HttpsProxyClient"/> class.
		/// </remarks>
		/// <param name="host">The host name of the proxy server.</param>
		/// <param name="port">The proxy server port.</param>
		/// <param name="credentials">The credentials to use to authenticate with the proxy server.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="host"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="credentials"/>is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>1</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>The <paramref name="host"/> is a zero-length string.</para>
		/// <para>-or-</para>
		/// <para>The length of <paramref name="host"/> is greater than 255 characters.</para>
		/// </exception>
		public HttpsProxyClient (string host, int port, NetworkCredential credentials) : base (host, port, credentials)
		{
			SslProtocols = DefaultSslProtocols;
		}

		/// <summary>
		/// Gets or sets the set of enabled SSL and/or TLS protocol versions that the client is allowed to use.
		/// </summary>
		/// <remarks>
		/// <para>Gets or sets the enabled SSL and/or TLS protocol versions that the client is allowed to use.</para>
		/// <para>By default, MailKit initializes this value to enable only TLS v1.2 and greater.
		/// TLS v1.1, TLS v1.0 and all versions of SSL are not enabled by default due to them all being
		/// susceptible to security vulnerabilities such as POODLE.</para>
		/// <para>This property should be set before calling any of the
		/// <a href="Overload_MailKit_Net_Proxy_ProxyClient_Connect.htm">Connect</a> methods.</para>
		/// </remarks>
		/// <value>The SSL and TLS protocol versions that are enabled.</value>
		public SslProtocols SslProtocols {
			get; set;
		}

#if NET5_0_OR_GREATER
		/// <summary>
		/// Gets or sets the cipher suites allowed to be used when negotiating an SSL or TLS connection.
		/// </summary>
		/// <remarks>
		/// Specifies the cipher suites allowed to be used when negotiating an SSL or TLS connection.
		/// When set to <c>null</c>, the operating system default is used. Use extreme caution when
		/// changing this setting.
		/// </remarks>
		/// <value>The cipher algorithms allowed for use when negotiating SSL or TLS encryption.</value>
		public CipherSuitesPolicy SslCipherSuitesPolicy {
			get; set;
		}
#endif

		/// <summary>
		/// Gets or sets the client SSL certificates.
		/// </summary>
		/// <remarks>
		/// <para>Some servers may require the client SSL certificates in order
		/// to allow the user to connect.</para>
		/// <para>This property should be set before calling any of the
		/// <a href="Overload_MailKit_Net_Proxy_ProxyClient_Connect.htm">Connect</a> methods.</para>
		/// </remarks>
		/// <value>The client SSL certificates.</value>
		public X509CertificateCollection ClientCertificates {
			get; set;
		}

		/// <summary>
		/// Get or set whether connecting via SSL/TLS should check certificate revocation.
		/// </summary>
		/// <remarks>
		/// <para>Gets or sets whether connecting via SSL/TLS should check certificate revocation.</para>
		/// <para>Normally, the value of this property should be set to <c>true</c> (the default) for security
		/// reasons, but there are times when it may be necessary to set it to <c>false</c>.</para>
		/// <para>For example, most Certificate Authorities are probably pretty good at keeping their CRL and/or
		/// OCSP servers up 24/7, but occasionally they do go down or are otherwise unreachable due to other
		/// network problems between the client and the Certificate Authority. When this happens, it becomes
		/// impossible to check the revocation status of one or more of the certificates in the chain
		/// resulting in an <see cref="Security.SslHandshakeException"/> being thrown in the
		/// <a href="Overload_MailKit_Net_Proxy_ProxyClient_Connect.htm">Connect</a> method. If this becomes a problem,
		/// it may become desirable to set <see cref="CheckCertificateRevocation"/> to <c>false</c>.</para>
		/// </remarks>
		/// <value><c>true</c> if certificate revocation should be checked; otherwise, <c>false</c>.</value>
		public bool CheckCertificateRevocation {
			get; set;
		}

		/// <summary>
		/// Get or sets a callback function to validate the server certificate.
		/// </summary>
		/// <remarks>
		/// <para>Gets or sets a callback function to validate the server certificate.</para>
		/// <para>This property should be set before calling any of the
		/// <a href="Overload_MailKit_Net_Proxy_ProxyClient_Connect.htm">Connect</a> methods.</para>
		/// </remarks>
		/// <value>The server certificate validation callback function.</value>
		public RemoteCertificateValidationCallback ServerCertificateValidationCallback {
			get; set;
		}

		// Note: This is used by SslHandshakeException to build the exception message.
		SslCertificateValidationInfo SslCertificateValidationInfo;

		bool ValidateRemoteCertificate (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			bool valid;

			SslCertificateValidationInfo?.Dispose ();
			SslCertificateValidationInfo = null;

			if (ServerCertificateValidationCallback != null) {
				valid = ServerCertificateValidationCallback (ProxyHost, certificate, chain, sslPolicyErrors);
#if !NETSTANDARD1_3 && !NETSTANDARD1_6
			} else if (ServicePointManager.ServerCertificateValidationCallback != null) {
				valid = ServicePointManager.ServerCertificateValidationCallback (ProxyHost, certificate, chain, sslPolicyErrors);
#endif
			} else {
				valid = sslPolicyErrors == SslPolicyErrors.None;
			}

			if (!valid) {
				// Note: The SslHandshakeException.Create() method will nullify this once it's done using it.
				SslCertificateValidationInfo = new SslCertificateValidationInfo (sender, certificate, chain, sslPolicyErrors);
			}

			return valid;
		}

#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
		/// <summary>
		/// Gets the SSL/TLS client authentication options for use with .NET5's SslStream.AuthenticateAsClient() API.
		/// </summary>
		/// <remarks>
		/// Gets the SSL/TLS client authentication options for use with .NET5's SslStream.AuthenticateAsClient() API.
		/// </remarks>
		/// <param name="host">The target host that the client is connected to.</param>
		/// <param name="remoteCertificateValidationCallback">The remote certificate validation callback.</param>
		/// <returns>The client SSL/TLS authentication options.</returns>
		SslClientAuthenticationOptions GetSslClientAuthenticationOptions (string host, RemoteCertificateValidationCallback remoteCertificateValidationCallback)
		{
			return new SslClientAuthenticationOptions {
				CertificateRevocationCheckMode = CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
				ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http11 },
				RemoteCertificateValidationCallback = remoteCertificateValidationCallback,
#if NET5_0_OR_GREATER
				CipherSuitesPolicy = SslCipherSuitesPolicy,
#endif
				ClientCertificates = ClientCertificates,
				EnabledSslProtocols = SslProtocols,
				TargetHost = host
			};
		}
#endif

		async Task<Stream> ConnectAsync (string host, int port, bool doAsync, CancellationToken cancellationToken)
		{
			ValidateArguments (host, port);

			cancellationToken.ThrowIfCancellationRequested ();

			var socket = await SocketUtils.ConnectAsync (ProxyHost, ProxyPort, LocalEndPoint, doAsync, cancellationToken).ConfigureAwait (false);
			var ssl = new SslStream (new NetworkStream (socket, true), false, ValidateRemoteCertificate);

			try {
				if (doAsync) {
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
					await ssl.AuthenticateAsClientAsync (GetSslClientAuthenticationOptions (host, ValidateRemoteCertificate), cancellationToken).ConfigureAwait (false);
#else
					await ssl.AuthenticateAsClientAsync (host, ClientCertificates, SslProtocols, CheckCertificateRevocation).ConfigureAwait (false);
#endif
				} else {
#if NETSTANDARD1_3 || NETSTANDARD1_6
					ssl.AuthenticateAsClientAsync (host, ClientCertificates, SslProtocols, CheckCertificateRevocation).GetAwaiter ().GetResult ();
#elif NET5_0_OR_GREATER
					ssl.AuthenticateAsClient (GetSslClientAuthenticationOptions (host, ValidateRemoteCertificate));
#else
					ssl.AuthenticateAsClient (host, ClientCertificates, SslProtocols, CheckCertificateRevocation);
#endif
				}
			} catch (Exception ex) {
				ssl.Dispose ();

				throw SslHandshakeException.Create (ref SslCertificateValidationInfo, ex, false, "HTTP", host, port, 443, 80);
			}

			var command = HttpProxyClient.GetConnectCommand (host, port, ProxyCredentials);

			try {
				if (doAsync)
					await ssl.WriteAsync (command, 0, command.Length, cancellationToken).ConfigureAwait (false);
				else
					ssl.Write (command, 0, command.Length);

				var buffer = ArrayPool<byte>.Shared.Rent (BufferSize);
				var builder = new StringBuilder ();

				try {
					var endOfHeaders = false;
					var newline = false;

					// read until we consume the end of the headers (it's ok if we read some of the content)
					do {
						int nread;

						if (doAsync)
							nread = await ssl.ReadAsync (buffer, 0, BufferSize, cancellationToken).ConfigureAwait (false);
						else
							nread = ssl.Read (buffer, 0, BufferSize);

						if (nread > 0) {
							int n = nread;

							for (int i = 0; i < nread && !endOfHeaders; i++) {
								switch ((char) buffer[i]) {
								case '\r':
									break;
								case '\n':
									endOfHeaders = newline;
									newline = true;

									if (endOfHeaders)
										n = i + 1;
									break;
								default:
									newline = false;
									break;
								}
							}

							builder.Append (Encoding.UTF8.GetString (buffer, 0, n));
						}
					} while (!endOfHeaders);
				} finally {
					ArrayPool<byte>.Shared.Return (buffer);
				}

				int index = 0;

				while (builder[index] != '\n')
					index++;

				if (index > 0 && builder[index - 1] == '\r')
					index--;

				// trim everything beyond the "HTTP/1.1 200 ..." part of the response
				builder.Length = index;

				var response = builder.ToString ();

				if (response.Length >= 15 && response.StartsWith ("HTTP/1.", StringComparison.OrdinalIgnoreCase) &&
					(response[7] == '1' || response[7] == '0') && response[8] == ' ' &&
					response[9] == '2' && response[10] == '0' && response[11] == '0' &&
					response[12] == ' ') {
					return ssl;
				}

				throw new ProxyProtocolException (string.Format (CultureInfo.InvariantCulture, "Failed to connect to {0}:{1}: {2}", host, port, response));
			} catch {
				ssl.Dispose ();
				throw;
			}
		}

		/// <summary>
		/// Connect to the target host.
		/// </summary>
		/// <remarks>
		/// Connects to the target host and port through the proxy server.
		/// </remarks>
		/// <returns>The connected network stream.</returns>
		/// <param name="host">The host name of the proxy server.</param>
		/// <param name="port">The proxy server port.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="host"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>0</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="host"/> is a zero-length string.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.Net.Sockets.SocketException">
		/// A socket error occurred trying to connect to the remote host.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		public override Stream Connect (string host, int port, CancellationToken cancellationToken = default (CancellationToken))
		{
			return ConnectAsync (host, port, false, cancellationToken).GetAwaiter ().GetResult ();
		}

		/// <summary>
		/// Asynchronously connect to the target host.
		/// </summary>
		/// <remarks>
		/// Asynchronously connects to the target host and port through the proxy server.
		/// </remarks>
		/// <returns>The connected network stream.</returns>
		/// <param name="host">The host name of the proxy server.</param>
		/// <param name="port">The proxy server port.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="host"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>0</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="host"/> is a zero-length string.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.Net.Sockets.SocketException">
		/// A socket error occurred trying to connect to the remote host.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		public override Task<Stream> ConnectAsync (string host, int port, CancellationToken cancellationToken = default (CancellationToken))
		{
			return ConnectAsync (host, port, true, cancellationToken);
		}
	}
}