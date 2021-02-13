// https://github.com/samueleresca/Blog.FSharpOnWeb/blob/master/test/Blog.FSharpWebAPI.Tests/Fixtures.fs

module Fixtures

open Xunit
open Newtonsoft.Json
open Newtonsoft.Json.Serialization

type Link = { Url: string; ChannelId: string; Via: string  }


let getLink = { Url = "https://cosmic-chimps.com"; ChannelId = ""; Via = "web"  }

let shouldContains actual expected = Assert.Contains(actual, expected)
let shouldEqual expected actual = Assert.Equal(expected, actual)
let shouldNotNull expected = Assert.NotNull(expected)
let shouldEqualStatusCode (expected: System.Net.HttpStatusCode) (actual: System.Net.HttpStatusCode) = Assert.Equal(expected, actual)

let serializeObject obj =
    let settings =
        JsonSerializerSettings(ContractResolver = CamelCasePropertyNamesContractResolver())

    JsonConvert.SerializeObject(getLink, settings)
