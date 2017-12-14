﻿using Microsoft.CodeAnalysis.Elfie.Model.Strings;
using XForm.Data;

namespace XForm.Functions.String
{
    internal class ToUpperBuilder : IFunctionBuilder
    {
        public string Name => "ToUpper";
        public string Usage => "ToUpper([String8])";

        public IDataBatchColumn Build(IDataBatchEnumerator source, WorkflowContext context)
        {
            // Create a String8Block to hold the uppercase copy of the values
            String8Block block = new String8Block();

            return new SimpleTransformFunction<String8, String8>(
                context.Parser.NextColumn(source, context),
                (string8) =>
                {
                    // Make a copy, make it uppercase, and return it
                    String8 upper = block.GetCopy(string8);
                    upper.ToUpperInvariant();
                    return upper;
                },
                () =>
                {
                    // Before each page, clear the String8Block to reuse the memory
                    block.Clear();
                }
            );
        }
    }
}
