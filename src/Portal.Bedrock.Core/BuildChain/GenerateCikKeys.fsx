open System
open System.IO

let getKeyBytes (envVarName: string) =
    let envValue = Environment.GetEnvironmentVariable(envVarName)

    if String.IsNullOrEmpty(envValue) then
        None
    else
        let cleanHex = envValue.Replace("0x", "").Replace(" ", "").ToUpper()

        if cleanHex.Length % 2 <> 0 || not <| System.Text.RegularExpressions.Regex.IsMatch(cleanHex, "^[0-9A-F]+$") then
            failwithf "Environment variable %s has invalid hex string: %s" envVarName envValue
        else
            Some [| for i in 0..2..(cleanHex.Length - 1) -> Convert.ToByte(cleanHex.Substring(i, 2), 16) |]

let generateByteArrayCode (bytes: byte[]) =
    if bytes.Length = 0 then "new byte[]{0x00}"
    else
        let hexStrings = bytes |> Array.map (fun b -> sprintf "0x%02X" b)
        sprintf "new byte[]{%s}" (String.Join(",", hexStrings))

let generateCSharpCode (preBytes: byte[]) (relBytes: byte[]) =
    let preCode = generateByteArrayCode preBytes
    let relCode = generateByteArrayCode relBytes

    sprintf """namespace Portal.Bedrock.Core
{
    internal static class CikKeys
    {
        public static readonly byte[] Preview = %s;
        public static readonly byte[] Release = %s;
    }
}""" preCode relCode

let main (argv: string[]) =
    let outputPath =
        let idx = Array.IndexOf(argv, "--out")
        if idx >= 0 && idx + 1 < argv.Length then argv.[idx + 1]
        else failwith "Missing --out <path>"

    let preBytes = getKeyBytes "PRE_MC_KEY"
    let relBytes = getKeyBytes "REL_MC_KEY"

    let effectivePre, effectiveRel =
        match preBytes, relBytes with
        | Some p, Some r ->
            printfn "PRE_MC_KEY byte count: %d, REL_MC_KEY byte count: %d" p.Length r.Length
            p, r
        | _ ->
            printfn "WARNING: PRE_MC_KEY/REL_MC_KEY not set; writing placeholder CIK keys (0x00)."
            [||], [||]

    let content = generateCSharpCode effectivePre effectiveRel
    let already = File.Exists(outputPath) && File.ReadAllText(outputPath) = content
    if not already then
        File.WriteAllText(outputPath, content)
        printfn "Generated: %s" outputPath
    else
        printfn "Unchanged: %s" outputPath

main (Environment.GetCommandLineArgs() |> Array.skip 1)
