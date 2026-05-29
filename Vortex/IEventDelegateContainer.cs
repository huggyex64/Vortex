using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vortex {


public interface IEventDelegateContainer : IDisposable
{
    public bool Enabled { get; }
    public void Enable();
    public void Disable();

    public bool Added { get; }
    public bool HasTag(string tag);
}


}
