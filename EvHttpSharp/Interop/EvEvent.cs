using System;
using System.Runtime.InteropServices;

namespace EvHttpSharp.Interop
{
	class EvEvent : SafeHandle
	{
		private string _created;
		public EvEvent() : base(IntPtr.Zero, true)
		{
			_created = Environment.StackTrace;
		}

		readonly object _lock = new object();
		private bool _released;

		protected override bool ReleaseHandle()
		{
			lock (_lock)
			{
				if (!_released)
					Event.EventFree (handle);
				else
				{
					return true;
				}
				_released = true;
			}
			
			return true;
		}

		public override bool IsInvalid
		{
			get { return handle == IntPtr.Zero; }
		}
	}
}
