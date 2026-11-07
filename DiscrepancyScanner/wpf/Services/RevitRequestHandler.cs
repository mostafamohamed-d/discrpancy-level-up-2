using System;
using Autodesk.Revit.UI;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Runs work inside a real Revit API context on behalf of the modeless check window.
    ///
    /// Revit refuses interactive operations such as Selection.PickPoint when they are called
    /// straight from a modeless window's event handler, so every request is queued through an
    /// ExternalEvent and executed the next time Revit is idle.
    /// </summary>
    public class RevitRequestHandler : IExternalEventHandler
    {
        private ExternalEvent externalEvent;
        private Action<UIApplication> pendingRequest;

        /// <summary>
        /// Must be called from a Revit API context, which in practice means the external
        /// command's Execute method.
        /// </summary>
        public static RevitRequestHandler Create()
        {
            RevitRequestHandler handler = new RevitRequestHandler();
            handler.externalEvent = ExternalEvent.Create(handler);
            return handler;
        }

        /// <summary>Queues work to run on the Revit thread. Returns immediately.</summary>
        public void Raise(Action<UIApplication> request)
        {
            pendingRequest = request;
            externalEvent?.Raise();
        }

        public void Execute(UIApplication application)
        {
            Action<UIApplication> request = pendingRequest;
            pendingRequest = null;
            request?.Invoke(application);
        }

        public string GetName()
        {
            return "Discrepancy Scanner model check";
        }
    }
}
