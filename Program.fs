module fsharp_giraffe.App

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Giraffe
open FSharp.Control.Tasks
open System.IdentityModel.Tokens.Jwt;
open Microsoft.AspNetCore.Authentication.OpenIdConnect
open Microsoft.AspNetCore.Authentication.JwtBearer
open Microsoft.IdentityModel.Tokens
open Microsoft.AspNetCore.Authorization


// ---------------------------------
// Models
// ---------------------------------

type Message =
    {
        Text : string
    }

// ---------------------------------
// Views
// ---------------------------------

module Views =
    open Giraffe.ViewEngine

    let layout (content: XmlNode list) =
        html [] [
            head [] [
                title []  [ encodedText "fsharp_giraffe" ]
                link [ _rel  "stylesheet"
                       _type "text/css"
                       _href "/main.css" ]
            ]
            body [] content
        ]

    let partial () =
        h1 [] [ encodedText "fsharp_giraffe" ]

    let index (model : Message) =
        [
            partial()
            p [] [ encodedText model.Text ]
        ] |> layout

// ---------------------------------
// Web app
// ---------------------------------

let indexHandler (name : string) =
    let greetings = sprintf "Hello %s, from Giraffe!" name
    let model     = { Text = greetings }
    let view      = Views.index model
    htmlView view

let indexHandlerJson (name : string) =
    let greetings = sprintf "Hello %s, from Giraffe!" name
    json greetings

let writeHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        ctx.WriteTextAsync "Hello World"


let personHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            return! json "text" next ctx
        }

let sayHelloWorld : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        let name =
            ctx.TryGetQueryStringValue "name"
            |> Option.defaultValue "Giraffe"
        let greeting = sprintf "Hello World, from %s" name
        json greeting next ctx

let checkUserIsLoggedIn : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            if isNotNull ctx.User && ctx.User.Identity.IsAuthenticated
            then return! next ctx
            else
                ctx.SetStatusCode 401
                return Some ctx
        }

let sayHelloWorldJson : HttpHandler = json "Hello from json"
let sayHelloWorldJsonParam (name:string) : HttpHandler = json name

[<CLIMutable>] (*this will create a parameterless constructor dor type*)
type Person = { Name:string;Age:int}

let submitFooHandler : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! person = ctx.BindJsonAsync<Person>()
            let greeting = sprintf "Hello World, from %s" person.Name
            return! text greeting next ctx
        }

// let notLoggedIn =
//     RequestErrors.UNAUTHORIZED
//         "Cookie"
//         "Some Realm"
//         "You must be logged in."

// let mustBeLoggedIn = requiresAuthentication notLoggedIn

//oidc
let authenticateOidc : HttpHandler =
  challenge "oidc"
  |> requiresAuthentication

let signInOidc (next : HttpFunc) (ctx : HttpContext) =
  authenticateOidc next ctx

//bearer
let authenticateBearer : HttpHandler =
  challenge "Bearer"
  |> requiresAuthentication

let signInBearer (next : HttpFunc) (ctx : HttpContext) =
  authenticateBearer next ctx

let webApp =
    choose [
        GET >=>
            choose [
                route "/" >=> indexHandler "world"
                routef "/hello/%s" indexHandler
                routef "/json/%s" indexHandlerJson
                //oidc
                route "/jsonOidc" >=> signInOidc >=> sayHelloWorldJson
                //bearer
                route "/jsonBearer" >=> signInBearer >=> sayHelloWorldJson
                route "/jsonwithparam" >=>  sayHelloWorld 
                subRoute "/api"
                    (choose [
                        subRoute "/v1"
                            (choose [
                                route "/foo" >=> text "Foo 1"
                                route "/bar" >=> text "Bar 1" ])
                        subRoute "/v2"
                            (choose [
                                route "/foo" >=> text "Foo 2"
                                route "/bar" >=> text "Bar 2" ]) ])
                subRoutef "/%s/api" (fun lang ->
                    requiresAuthentication (challenge "Cookie") >=>
                        choose [
                            route  "/blah" >=> text "blah"
                            routef "/%s" (fun n -> text (sprintf "Hello %s! Lang: %s" n lang))
                        ])
                // route "/json" >=> 
                //     requiresAuthentication (challenge "Cookie") >=>
                //         choose [
                //             GET >=> sayHelloWorldJson
                //         ]
                        ]
                
        POST >=> 
            choose [
                route "/foo" >=> submitFooHandler
            ]
        setStatusCode 404 >=> text "Not Found" ]


// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder : CorsPolicyBuilder) =
    builder
        .WithOrigins(
            "http://localhost:5000",
            "https://localhost:5001")
       .AllowAnyMethod()
       .AllowAnyHeader()
       |> ignore

let configureApp (app : IApplicationBuilder) =
    JwtSecurityTokenHandler.DefaultMapInboundClaims <- false;
    let env = app.ApplicationServices.GetService<IWebHostEnvironment>()
    (match env.IsDevelopment() with
    | true  ->
        app.UseDeveloperExceptionPage()
    | false ->
        app.UseGiraffeErrorHandler(errorHandler)
            .UseHttpsRedirection())
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseAuthentication()
        .UseAuthorization()
        .UseGiraffe(webApp)

let configureServices (services : IServiceCollection) =
    services.AddCors()    |> ignore
    services.AddAuthorization() |> ignore
    
    //OIDC 
    // services.AddAuthentication(fun options ->
    //         options.DefaultScheme <- "Cookies";
    //         options.DefaultChallengeScheme <- "oidc";
    //     )
    //     .AddCookie("Cookies")
    //     .AddOpenIdConnect("oidc", fun options ->
    //         options.Authority <- "http://identity.decky.eu";

    //         options.ClientId <- "mvcclient2";
    //         options.ClientSecret <- "secret";
    //         options.ResponseType <- "code";
    //         options.RequireHttpsMetadata <- false;
    //         options.SaveTokens <- true;
    //         options.UsePkce <- true;
    //     ) |> ignore

    //bearer
    services.AddAuthentication("Bearer")
        .AddJwtBearer("Bearer", fun options ->
            options.Authority <- "http://identity.decky.eu"
            options.RequireHttpsMetadata <- false
            options.TokenValidationParameters <- TokenValidationParameters(ValidateAudience = false)
        ) |> ignore

    services.AddAuthorization(fun options -> 
        options.AddPolicy("local-api-scope", fun builder -> 
                builder.RequireAuthenticatedUser() |> ignore 
                builder.RequireClaim("scope", "local-api-scope") |> ignore
            )
    ) |> ignore

    services.AddGiraffe() |> ignore 

let configureLogging (builder : ILoggingBuilder) =
    builder.AddConsole()
           .AddDebug() |> ignore

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    Host.CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(
            fun webHostBuilder ->
                webHostBuilder
                    .UseContentRoot(contentRoot)
                    .UseWebRoot(webRoot)
                    .Configure(Action<IApplicationBuilder> configureApp)
                    .ConfigureServices(configureServices)
                    .ConfigureLogging(configureLogging)
                    |> ignore)
        .Build()
        .Run()
    0