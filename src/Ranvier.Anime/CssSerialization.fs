namespace Ranvier.Anime

open System
open System.Buffers
open System.Collections.Generic

/// <summary>Lossless declaration boundaries in native CSSOM serialization; values remain native CSS text.</summary>
module internal CssSerialization =
    let private specialChars = SearchValues.Create("\\/*'\";:!()[]{}")

    let private commitSegment (span: ReadOnlySpan<char>) (declStart: int) (firstColon: int) (lastBang: int) (ending: int) (result: ResizeArray<struct(string * string * string)>) =
        if firstColon >= declStart then
            let nameSpan = span.Slice(declStart, firstColon - declStart).Trim()
            if not nameSpan.IsEmpty then
                let mutable valueEnd = ending
                let mutable priority = ""
                if lastBang > firstColon && lastBang < ending then
                    let bangTail = span.Slice(lastBang + 1, ending - lastBang - 1).Trim()
                    if bangTail.Equals("important", StringComparison.OrdinalIgnoreCase) then
                        priority <- "important"
                        valueEnd <- lastBang
                let valueSpan = span.Slice(firstColon + 1, valueEnd - firstColon - 1).Trim()
                result.Add(struct(nameSpan.ToString(), valueSpan.ToString(), priority))

    /// <summary>Splits serialized declarations, retaining raw values and top-level important priority.</summary>
    let declarations (text:string) =
        if String.IsNullOrEmpty text then
            Array.Empty<struct(string * string * string)>()
        else
            let span = text.AsSpan()
            let len = span.Length
            let result = ResizeArray<struct(string * string * string)>(4)
            let mutable index = 0
            let mutable quote = '\000'
            let mutable depth = 0
            let mutable declStart = 0
            let mutable firstColon = -1
            let mutable lastBang = -1

            while index < len do
                if quote <> '\000' then
                    let rem = span.Slice(index)
                    let nextIdx = rem.IndexOfAny(quote, '\\')
                    if nextIdx < 0 then
                        index <- len
                    else
                        let hit = index + nextIdx
                        if span[hit] = '\\' then
                            index <- hit + 2
                        else
                            quote <- '\000'
                            index <- hit + 1
                else
                    let rem = span.Slice(index)
                    let nextSpecial = rem.IndexOfAny(specialChars)
                    if nextSpecial < 0 then
                        index <- len
                    else
                        index <- index + nextSpecial
                        let value = span[index]
                        if value = '\\' then
                            index <- index + 2
                        elif value = '/' && index + 1 < len && span[index + 1] = '*' then
                            let tail = span.Slice(index + 2)
                            let endRel = tail.IndexOf("*/".AsSpan(), StringComparison.Ordinal)
                            index <- if endRel < 0 then len else index + 2 + endRel + 2
                        elif value = '\'' || value = '"' then
                            quote <- value
                            index <- index + 1
                        elif value = '(' || value = '[' || value = '{' then
                            depth <- depth + 1
                            index <- index + 1
                        elif value = ')' || value = ']' || value = '}' then
                            if depth > 0 then depth <- depth - 1
                            index <- index + 1
                        elif depth = 0 then
                            if value = ';' then
                                commitSegment span declStart firstColon lastBang index result
                                declStart <- index + 1
                                firstColon <- -1
                                lastBang <- -1
                            elif value = ':' then
                                if firstColon < 0 then firstColon <- index
                            elif value = '!' then
                                if firstColon >= 0 then lastBang <- index
                            index <- index + 1
                        else
                            index <- index + 1

            if declStart <= len then
                commitSegment span declStart firstColon lastBang len result

            if result.Count = 0 then Array.Empty<struct(string * string * string)>()
            else result.ToArray()
