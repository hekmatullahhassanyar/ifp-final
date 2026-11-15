using System.Runtime.InteropServices;

namespace DeliveryQuote;

internal static class NativeMethods
{
    [DllImport("deliverynative", EntryPoint = "dq_apply_discount", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long ApplyDiscount(long amountCents, int percent);
}

public sealed class NativeDiscountAdapter
{
    private readonly Func<long, int, long> _nativeCall;

    public NativeDiscountAdapter() : this(NativeMethods.ApplyDiscount) { }

    public NativeDiscountAdapter(Func<long, int, long> nativeCall) => _nativeCall = nativeCall;

    public Result<long> Apply(long amountCents, int percent)
    {
        if (amountCents < 0)
            return Result.Fail<long>("INVALID_AMOUNT", $"Amount must not be negative (was {amountCents}).");
        if (percent is < 0 or > 100)
            return Result.Fail<long>("INVALID_DISCOUNT", $"Discount must be 0..100 (was {percent}).");

        try
        {
            var value = _nativeCall(amountCents, percent);
            return value switch
            {
                >= 0 => Result.Ok(value),
                -1 => Result.Fail<long>("NATIVE_INVALID_ARGUMENT", "Native library rejected the arguments."),
                -2 => Result.Fail<long>("NATIVE_OVERFLOW", "Native library reported an overflow."),
                _ => Result.Fail<long>("NATIVE_UNKNOWN_CODE", $"Native library returned unexpected code {value}.")
            };
        }
        catch (DllNotFoundException ex)
        {
            return Result.Fail<long>("NATIVE_LIBRARY_NOT_FOUND",
                $"Native library 'deliverynative' could not be loaded. Build it with native/build.sh (or build.bat). {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            return Result.Fail<long>("NATIVE_ENTRY_POINT_NOT_FOUND",
                $"Function 'dq_apply_discount' was not found in the native library. {ex.Message}");
        }
        catch (BadImageFormatException ex)
        {
            return Result.Fail<long>("NATIVE_ARCHITECTURE_MISMATCH",
                $"Native library has the wrong architecture for this process. {ex.Message}");
        }
    }
}
