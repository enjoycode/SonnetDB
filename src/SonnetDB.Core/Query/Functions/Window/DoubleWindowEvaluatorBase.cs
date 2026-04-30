using SonnetDB.Model;

namespace SonnetDB.Query.Functions.Window;

internal abstract class DoubleWindowEvaluatorBase : IWindowDoubleEvaluator
{
    public abstract string FieldName { get; }

    public abstract WindowDoubleOutput ComputeDouble(long[] timestamps, FieldValue?[] values);

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var typed = ComputeDouble(timestamps, values);
        var boxed = new object?[typed.Length];
        for (int i = 0; i < boxed.Length; i++)
            boxed[i] = typed.HasValue[i] ? typed.Values[i] : null;
        return boxed;
    }
}
