﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using EvHttpSharp;
using Nancy.Bootstrapper;
using Nancy.IO;

namespace Nancy.Hosting.Event2
{
	public class NancyEvent2Host : IDisposable
	{
		private readonly string _host;
		private readonly int _port;
		private readonly int _workers;
		private readonly INancyBootstrapper _bootstrapper;
		private readonly INancyEngine _engine;
		private EventHttpListener _listener;

		public NancyEvent2Host(string host, int port, int workers, INancyBootstrapper bootstrapper)
		{
			_host = host;
			_port = port;
			_workers = workers;
			_bootstrapper = bootstrapper;
			_bootstrapper.Initialise();
			_engine = _bootstrapper.GetEngine();
		}

		public void Start()
		{
			_listener = new EventHttpListener(RequestHandler);
			_listener.Start(_host, (ushort) _port, _workers);
		}

		public void Stop()
		{
			if (_listener == null)
				return;
			_listener.Dispose ();
			_listener = null;
		}

		private void RequestHandler(EventHttpRequest req)
		{
			ThreadPool.QueueUserWorkItem(_ =>
				{
					var pairs = req.Uri.Split(new[] {'?'}, 2);
					var path = Uri.UnescapeDataString(pairs[0]);
					var query = pairs.Length == 2 ? pairs[1] : string.Empty;
					var nreq = new Request(req.Method, path, req.Headers,
					                             RequestStream.FromStream(new MemoryStream(req.RequestBody)), "http", query);

					var ctx = _engine.HandleRequest(nreq);
					PostProcessNancyResponse(nreq, ctx.Response);

					var ms = new MemoryStream();
					ctx.Response.Contents(ms);
					req.Respond((System.Net.HttpStatusCode) ctx.Response.StatusCode, ctx.Response.Headers, ms.ToArray());

				});
		}

		protected virtual void PostProcessNancyResponse (Request request, Response response)
		{
			response.Headers["Content-Type"] = response.ContentType;
		}

		public void Dispose()
		{
			Stop();
		}
	}
}
