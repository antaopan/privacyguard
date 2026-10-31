using System;
using System.ServiceProcess;

namespace PrivacyGuard
{
    public class PrivacyGuardService : ServiceBase
    {
        private Engine _engine;

        public PrivacyGuardService()
        {
            ServiceName = ProductInfo.ServiceName;
            CanStop = true;
            CanShutdown = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                _engine = new Engine();
                _engine.Start();
            }
            catch (Exception ex)
            {
                Logger.Error("service", "ONSTART_FAILED", ex.ToString());
                throw;
            }
        }

        protected override void OnStop()
        {
            try
            {
                if (_engine != null)
                {
                    _engine.Stop();
                    _engine = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("service", "ONSTOP_FAILED", ex.ToString());
            }
        }

        protected override void OnShutdown()
        {
            OnStop();
            base.OnShutdown();
        }
    }
}
