using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Station.Desktop.Services.Kiosk;
internal sealed class NoopKioskGuard : IKioskGuard
{
    public event Action? RequestExit;
    public bool IsHardened => false;
    public void Install() { }
    public void Uninstall() { }
    public void Dispose() { }
}
