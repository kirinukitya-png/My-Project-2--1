using System.Collections.Generic;

namespace ProjectNST.GameCore.Board
{
    public sealed class BoardHandStateValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public IReadOnlyList<string> Errors { get; }

        internal BoardHandStateValidationResult(IEnumerable<string> errors)
        {
            Errors = System.Array.AsReadOnly(new List<string>(errors).ToArray());
        }
    }
}
