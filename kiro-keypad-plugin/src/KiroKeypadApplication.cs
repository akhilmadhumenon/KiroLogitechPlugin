namespace Loupedeck.KiroKeypadPlugin
{
    using System;

    public class KiroKeypadApplication : ClientApplication
    {
        protected override String GetProcessName() => "";

        protected override String GetBundleName() => "";

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
