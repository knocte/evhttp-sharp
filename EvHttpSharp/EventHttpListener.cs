using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using EvHttpSharp.Interop;

namespace EvHttpSharp
{
	public class EventHttpListener : IDisposable
	{
		public delegate void RequestCallback(EventHttpRequest req);

		private readonly RequestCallback _requestHandler;
		
		private int _workers;
		private List<EventHttpWorker> _workerList;

		public EventHttpListener(RequestCallback requestHandler)
		{
			LibLocator.TryToLoadDefaultIfNotInitialized();
			_requestHandler = requestHandler;
		}

		public void Start(string host, ushort port, int workers = 1)
		{
			_workers = workers;
			var soaddr = new Event.sockaddr_in
				{
					sin_family = Event.AF_INET,
					sin_port = (ushort) IPAddress.HostToNetworkOrder((short) port),
					sin_addr = 0,
					sin_zero = new byte[8]
				};

			IntPtr fd;
			using(var evBase = Event.EventBaseNew())
			using (var listener = Event.EvConnListenerNewBind (evBase, IntPtr.Zero, IntPtr.Zero, 1u << 3, 256, ref soaddr,
			                                                  Marshal.SizeOf(soaddr)))
				fd = listener.FileDescriptor;
			
			_workerList = new List<EventHttpWorker> ();
			for (var c = 0; c < _workers; c++)
			{
				var worker = new EventHttpWorker (_requestHandler);
				worker.Start (fd);
				_workerList.Add (worker);
			}
		}

		public void Dispose()
		{
			foreach (var worker in _workerList)
				worker.Dispose ();
			
			
		}
	}
}
