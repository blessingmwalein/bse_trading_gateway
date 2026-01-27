using QuickFix;
using System;
using System.Globalization;
using QuickFix.Fields;
namespace MyFixFields
{
    public class SendingTimeMicros : DateTimeField
    {
        public SendingTimeMicros() : base(52) { }

        public SendingTimeMicros(DateTime dt) : base(52, dt, true) { }

        // Remove ValueToString override
        // Just provide helper to parse microsecond string
        public void SetFromString(string val)
        {
            if (DateTime.TryParseExact(val, "yyyyMMdd-HH:mm:ss.ffffff",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime dt))
            {
                this.Obj = dt;
            }
            else
            {
                throw new ArgumentException($"Invalid SendingTime format: {val}");
            }
        }

        public DateTime ToDateTime() => (DateTime)this.Obj;
    }
}