open System
open Microsoft.Extensions.Configuration
open System.IO
open FSharp.CosmosDb
open System.Text.Json.Serialization
open FSharp.Control

type Question =
    { [<Id>]
      id: string
      [<PartitionKey>]
      modelType: string
      question: string
      [<JsonPropertyName("correct_answer")>]
      correctAnswer: string
      [<JsonPropertyName("incorrect_answers")>]
      incorrectAnswers: string array }

type User =
    { [<Id>]
      id: string
      [<PartitionKey>]
      modelType: string
      name: string }

type Answer =
    { answer: string
      user: User
      question: Question }

type Game =
    { [<Id>]
      id: string
      [<PartitionKey>]
      modelType: string
      state: string
      players: User array
      questions: Question array
      answers: Answer array }

let getConnection connStr =
    Cosmos.fromConnectionString connStr
    |> Cosmos.database "trivia"
    |> Cosmos.container "game"

[<EntryPoint>]
let main argv =
    let environmentName =
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")

    let builder =
        JsonConfigurationExtensions.AddJsonFile(
            JsonConfigurationExtensions.AddJsonFile(
                FileConfigurationExtensions.SetBasePath(ConfigurationBuilder(), Directory.GetCurrentDirectory()),
                "appsettings.json",
                true,
                true
            ),
            sprintf "appsettings.%s.json" environmentName,
            true,
            true
        )

    let config = builder.Build()

    async {
        let connection =
            config.["CosmosConnection:ConnectionString"]
            |> getConnection

        let! game =
            connection
            |> Cosmos.query "SELECT TOP 10 * FROM c WHERE c.modelType = 'Question'"
            |> Cosmos.execAsync<Question>
            |> AsyncSeq.fold
                (fun state question ->
                    printfn "Question %d" ((state.questions |> Array.length) + 1)
                    printfn "	%s" question.question
                    printfn "--------"

                    let sorted =
                        question.incorrectAnswers
                        |> Array.append [| question.correctAnswer |]
                        |> Array.sort

                    sorted |> Array.iteri (printfn "	%d - %s")
                    printfn "--------"

                    printf "Select an answer: "
                    let selected = Console.ReadLine() |> Int32.Parse

                    { state with
                          questions = Array.append [| question |] state.questions
                          answers =
                              Array.append
                                  [| { answer = sorted.[selected]
                                       user = state.players.[0]
                                       question = question } |]
                                  state.answers })
                { id = Guid.NewGuid().ToString()
                  modelType = "Game"
                  state = "Started"
                  players =
                      [| { id = "CosmosDB Conf"
                           modelType = "User"
                           name = "CosmosDB Conf" } |]
                  answers = Array.empty
                  questions = Array.empty }

        do!
            connection
            |> Cosmos.insert game
            |> Cosmos.execAsync
            |> AsyncSeq.iter (fun _ -> printfn "Game has been created")

        return ()
    }
    |> Async.RunSynchronously

    0 // return an integer exit code
