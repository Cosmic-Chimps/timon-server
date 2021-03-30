module Password

open System

module PasswordGenerator =
    let rnd = new Random()
    /// Removes an element form a list.
    let remove n lst = lst |> List.filter (fun x -> x <> n)

    /// Randomly permutes the elements of a list.
    let rec scramble (lst : char list) =
        [
            let x = lst.[rnd.Next(0, lst.Length - 1)]
            yield x
            let lst' = remove x lst
            if not lst'.IsEmpty then
                yield! scramble lst'
        ]

    // Characters to use.
    let alpha = ['a' .. 'z']
    let upper = ['A' .. 'Z']
    let digits = ['1' .. '9']
    let punct = ['~'; '`'; '!'; '@'; '#'; '$'; '%'; '^'; '&'; '*'; '('; ')'; '-'; '_'; '+'; '='; '\"']


    /// Generates a new password of the specified length from the given list of characters.
    let genPass len =
        let chars =
            List.concat [alpha; upper; digits; punct ]

        let chars' = scramble chars

        let pass =
            [|
                for x in 1 .. len do
                    let rand = rnd.Next(0, chars.Length)
                    yield chars'.[rand]
            |]
        new String(pass)

    // Password checking
    let totalChars = 95
    let alphaLen = alpha.Length
    let upperLen = upper.Length
    let digitsLen = digits.Length
    let punctLen = punct.Length
    let otherChars = totalChars - (alphaLen + upperLen + digitsLen + punctLen)

    /// Evaluates the strength of a password.
    let evalPass pass =
        let evalChar (c : char) =
            match c with
            | _ when alpha |> List.exists (fun x -> x = c) -> alphaLen
            | _ when upper |> List.exists (fun x -> x = c) -> upperLen
            | _ when digits |> List.exists (fun x -> x = c) -> digitsLen
            | _ when punct |> List.exists (fun x -> x = c) -> punctLen
            | _ -> otherChars

        let score = pass |> Array.sumBy evalChar

        let score' = (log <| float score) * (float pass.Length / log 2.) |> floor

        let evalScore (bits : float) =
            match bits with
            | _ when bits >= 128. -> "Very Strong"
            | _ when bits < 128. && bits >= 64. -> "Strong"
            | _ when bits < 64. && bits >= 56. -> "Medium"
            | _ -> "Weak"

        evalScore score'


printf "%s" (PasswordGenerator.genPass 10)
