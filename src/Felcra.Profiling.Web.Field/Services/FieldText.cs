using Felcra.Profiling.Domain.Records;

namespace Felcra.Profiling.Web.Field.Services;

public static class FieldText
{
    public static string Measurement(MeasurementType type, decimal value) => type switch
    {
        MeasurementType.Weight => $"Weight {value:0.#} kg",
        MeasurementType.BodyCondition => $"BCS {value:0.#}",
        MeasurementType.MilkYield => $"Milk {value:0.#} L",
        _ => $"{type} {value}",
    };
}
