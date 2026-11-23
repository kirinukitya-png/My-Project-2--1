using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Synergy
{
    /// <summary>Immutable, deterministic result of board Unit and Attribute detection.</summary>
    public sealed class SynergyDetectionResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<UnitSynergyResult> EstablishedUnits { get; }
        public IReadOnlyList<AttributeSynergyResult> EstablishedAttributes { get; }
        public int AttributeBlockBonus { get; }

        internal SynergyDetectionResult(bool isValid, IList<string> errors,
            IList<UnitSynergyResult> units, IList<AttributeSynergyResult> attributes)
        {
            IsValid = isValid;
            Errors = Array.AsReadOnly(Copy(errors));
            EstablishedUnits = Array.AsReadOnly(Copy(units));
            EstablishedAttributes = Array.AsReadOnly(Copy(attributes));
            AttributeBlockBonus = isValid ? EstablishedAttributes.Count : 0;
        }

        private static T[] Copy<T>(IList<T> values)
        {
            var copy = new T[values == null ? 0 : values.Count];
            if (values != null)
                for (int i = 0; i < values.Count; i++) copy[i] = values[i];
            return copy;
        }
    }
}
