using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EvHttpSharp.Interop;

namespace EvHttpSharp
{
	internal class EventHttpWorker : IDisposable
	{
		private readonly EventHttpListener.RequestCallback _cb;
		

		private EventBase _eventBase;
		private EvHttp _evHttp;
		private Thread _thread;
		private GCHandle _httpCallbackHandle;
		private EvUserEvent _syncCbUserEvent;
		private readonly Queue<Action> _syncCallbacks = new Queue<Action>();
		private bool _stop;
		private EvConnListener _listener;
		private IntPtr _fd;

		public EventHttpWorker(EventHttpListener.RequestCallback cb)
		{
			LibLocator.TryToLoadDefaultIfNotInitialized();
			_cb = cb;
		}

		public void Start(IntPtr fd)
		{
			_fd = fd;
			_thread = new Thread(MainCycle);
			var tcs = new TaskCompletionSource<int>();
			_thread.Start(tcs);
			tcs.Task.Wait();
		}

		private void StartInternal()
		{
			_eventBase = Event.EventBaseNew ();
			if (_eventBase.IsInvalid)
				throw new IOException ("Unable to create event_base");
			_evHttp = Event.EvHttpNew (_eventBase);
			if (_evHttp.IsInvalid)
			{
				Dispose ();
				throw new IOException ("Unable to create evhttp");
			}
			_listener = Event.EvConnListenerNew (_eventBase, IntPtr.Zero, IntPtr.Zero, 1u << 3, 256, _fd);
			_listener.Disown();
			var socket = Event.EvHttpBindListener (_evHttp, _listener);
			if (socket.IsInvalid)
			{
				Dispose ();
				throw new IOException ("Unable to bind to the specified address");
			}
		}

		private void MainCycle(object ptcs)
		{
			var tcs = (TaskCompletionSource<int>) ptcs;
			try
			{
				StartInternal();
			}
			catch (Exception e)
			{
				tcs.SetException(e);
				return;
			}
			tcs.SetResult(0);

			var cb = new Event.D.evhttp_request_callback (RequestHandler);
			_httpCallbackHandle = GCHandle.Alloc (cb);
			Event.EvHttpSetAllowedMethods (_evHttp, EvHttpCmdType.All);
			Event.EvHttpSetGenCb (_evHttp, cb, GCHandle.ToIntPtr (_httpCallbackHandle));

			using (_syncCbUserEvent = new EvUserEvent(_eventBase))
			{
				_syncCbUserEvent.Triggered += SyncCallback;
				while (!_stop)
				{
					Event.EventBaseDispatch(_eventBase);
				}
			}
			//We've recieved loopbreak from actual Dispose, so dispose now
			DoDispose ();
			_httpCallbackHandle.Free ();
		}

		private void SyncCallback(object sender, EventArgs eventArgs)
		{
			lock (_syncCallbacks)
				while (_syncCallbacks.Count != 0)
					_syncCallbacks.Dequeue()();
		}

		private void RequestHandler(IntPtr request, IntPtr arg)
		{
			var req = new EventHttpRequest (this, request);
			_cb (req);
		}

		internal void Sync(Action cb)
		{
			if (_syncCallbacks.Count == 0 && Thread.CurrentThread == _thread)
			{
				cb();
				return;
			}
			lock (_syncCallbacks)
				_syncCallbacks.Enqueue(cb);
			_syncCbUserEvent.Active();
		}

		private void DoDispose()
		{
			if (_evHttp != null && !_evHttp.IsInvalid)
				_evHttp.Dispose();
			if (_eventBase != null && !_eventBase.IsInvalid)
				_eventBase.Dispose();

		}

		public void Dispose()
		{
			if (_thread == null)
				DoDispose();
			else if (_eventBase != null && !_eventBase.IsClosed)
			{
				_stop = true;
				Sync(() => Event.EventBaseLoopbreak(_eventBase));
				if (_thread != Thread.CurrentThread)
					_thread.Join ();
			}

		}
	}
}
