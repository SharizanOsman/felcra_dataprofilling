using Felcra.Profiling.Domain.Records;

namespace Felcra.Profiling.Web.Field.Services;

public static class FieldText
{
    public static string Measurement(MeasurementType type, decimal value) => type switch
    {
        MeasurementType.Weight => T("Weight {0:0.#} kg", value),
        MeasurementType.BodyCondition => T("BCS {0:0.#}", value),
        MeasurementType.MilkYield => T("Milk {0:0.#} L", value),
        _ => $"{type} {value}",
    };
}
