using ChibiRuby.StdLib;

namespace ChibiRuby;

/// <summary>Activation entry points for the optional ChibiRuby.NIO modules. Each Define* is independent opt-in; none are called by <see cref="MRubyState.Create()"/>.</summary>
public static class MRubyStateNIOExtensions
{
    /// <summary>Registers Ruby <c>IO</c>, <c>File</c>, and <c>IOError</c>.</summary>
    /// <remarks>With a fiber scheduler installed, reads/writes from a non-root fiber park the fiber instead of blocking the host thread.</remarks>
    public static void DefineIO(this MRubyState mrb)
    {
        var ioClass = mrb.DefineClass(mrb.Intern("IO"u8), mrb.ObjectClass, MRubyVType.Object);
        mrb.DefineMethod(ioClass, mrb.Intern("read"u8), IOMembers.Read);
        mrb.DefineMethod(ioClass, mrb.Intern("write"u8), IOMembers.Write);
        mrb.DefineMethod(ioClass, mrb.Intern("close"u8), IOMembers.Close);
        mrb.DefineMethod(ioClass, mrb.Intern("closed?"u8), IOMembers.ClosedQ);

        var fileClass = mrb.DefineClass(mrb.Intern("File"u8), ioClass, MRubyVType.Object);
        mrb.DefineClassMethod(fileClass, mrb.Intern("open"u8), FileMembers.Open);
        mrb.DefineClassMethod(fileClass, mrb.Intern("read"u8), FileMembers.Read);
        mrb.DefineClassMethod(fileClass, mrb.Intern("write"u8), FileMembers.Write);
        mrb.DefineClassMethod(fileClass, mrb.Intern("exist?"u8), FileMembers.ExistQ);
        mrb.DefineClassMethod(fileClass, mrb.Intern("exists?"u8), FileMembers.ExistQ);

        mrb.DefineClass(mrb.Intern("IOError"u8), mrb.StandardErrorClass);
    }

    /// <summary>Registers the Ruby <c>HTTP</c> module, its companion classes (<c>Response</c>/<c>Headers</c>/<c>Body</c>), and the <c>HTTP::Error</c> hierarchy.</summary>
    /// <remarks>
    /// 4xx/5xx do not raise (matches <see cref="System.Net.Http.HttpClient"/>); transport failures raise
    /// <c>HTTP::ConnectionError</c>, timeouts <c>HTTP::TimeoutError</c>. With a fiber scheduler, requests
    /// park the calling fiber. <c>json:</c> / <c>#json</c> require <see cref="DefineJson"/>.
    /// </remarks>
    public static void DefineHttp(this MRubyState mrb)
    {
        var httpModule = mrb.DefineModule(mrb.Intern("HTTP"u8), mrb.ObjectClass);

        var httpError = mrb.DefineClass(mrb.Intern("Error"u8), mrb.StandardErrorClass, outer: httpModule);
        mrb.DefineClass(mrb.Intern("TimeoutError"u8), httpError, outer: httpModule);
        mrb.DefineClass(mrb.Intern("ConnectionError"u8), httpError, outer: httpModule);

        mrb.DefineClassMethod(httpModule, mrb.Intern("get"u8), HttpMembers.Get);
        mrb.DefineClassMethod(httpModule, mrb.Intern("post"u8), HttpMembers.Post);
        mrb.DefineClassMethod(httpModule, mrb.Intern("put"u8), HttpMembers.Put);
        mrb.DefineClassMethod(httpModule, mrb.Intern("patch"u8), HttpMembers.Patch);
        mrb.DefineClassMethod(httpModule, mrb.Intern("delete"u8), HttpMembers.Delete);
        mrb.DefineClassMethod(httpModule, mrb.Intern("head"u8), HttpMembers.Head);
        mrb.DefineClassMethod(httpModule, mrb.Intern("options"u8), HttpMembers.Options);
        mrb.DefineClassMethod(httpModule, mrb.Intern("request"u8), HttpMembers.Request);

        var responseClass = mrb.DefineClass(mrb.Intern("Response"u8), mrb.ObjectClass, MRubyVType.CSharpData, outer: httpModule);
        mrb.DefineMethod(responseClass, mrb.Intern("status"u8), HttpResponseMembers.Status);
        mrb.DefineMethod(responseClass, mrb.Intern("headers"u8), HttpResponseMembers.Headers);
        mrb.DefineMethod(responseClass, mrb.Intern("body"u8), HttpResponseMembers.Body);
        mrb.DefineMethod(responseClass, mrb.Intern("uri"u8), HttpResponseMembers.Uri);
        mrb.DefineMethod(responseClass, mrb.Intern("version"u8), HttpResponseMembers.Version);
        mrb.DefineMethod(responseClass, mrb.Intern("content_type"u8), HttpResponseMembers.ContentType);
        mrb.DefineMethod(responseClass, mrb.Intern("success?"u8), HttpResponseMembers.SuccessQ);
        mrb.DefineMethod(responseClass, mrb.Intern("redirect?"u8), HttpResponseMembers.RedirectQ);
        mrb.DefineMethod(responseClass, mrb.Intern("client_error?"u8), HttpResponseMembers.ClientErrorQ);
        mrb.DefineMethod(responseClass, mrb.Intern("server_error?"u8), HttpResponseMembers.ServerErrorQ);
        mrb.DefineMethod(responseClass, mrb.Intern("error?"u8), HttpResponseMembers.ErrorQ);
        mrb.DefineMethod(responseClass, mrb.Intern("ensure_success_status!"u8), HttpResponseMembers.EnsureSuccessStatusBang);
        mrb.DefineMethod(responseClass, mrb.Intern("json"u8), HttpResponseMembers.Json);
        mrb.DefineMethod(responseClass, mrb.Intern("inspect"u8), HttpResponseMembers.Inspect);
        mrb.DefineMethod(responseClass, mrb.Intern("to_s"u8), HttpResponseMembers.ToS);

        var headersClass = mrb.DefineClass(mrb.Intern("Headers"u8), mrb.ObjectClass, MRubyVType.CSharpData, outer: httpModule);
        mrb.DefineMethod(headersClass, mrb.Intern("[]"u8), HttpHeadersMembers.OpAref);
        mrb.DefineMethod(headersClass, mrb.Intern("[]="u8), HttpHeadersMembers.OpAset);
        mrb.DefineMethod(headersClass, mrb.Intern("key?"u8), HttpHeadersMembers.KeyQ);
        mrb.DefineMethod(headersClass, mrb.Intern("each"u8), HttpHeadersMembers.Each);
        mrb.DefineMethod(headersClass, mrb.Intern("to_h"u8), HttpHeadersMembers.ToH);
        mrb.DefineMethod(headersClass, mrb.Intern("size"u8), HttpHeadersMembers.Size);
        mrb.DefineMethod(headersClass, mrb.Intern("length"u8), HttpHeadersMembers.Size);
        mrb.DefineMethod(headersClass, mrb.Intern("inspect"u8), HttpHeadersMembers.Inspect);

        var bodyClass = mrb.DefineClass(mrb.Intern("Body"u8), mrb.ObjectClass, MRubyVType.CSharpData, outer: httpModule);
        mrb.DefineMethod(bodyClass, mrb.Intern("to_s"u8), HttpBodyMembers.ToS);
        mrb.DefineMethod(bodyClass, mrb.Intern("bytesize"u8), HttpBodyMembers.Bytesize);
        mrb.DefineMethod(bodyClass, mrb.Intern("content_type"u8), HttpBodyMembers.ContentType);
        mrb.DefineMethod(bodyClass, mrb.Intern("empty?"u8), HttpBodyMembers.EmptyQ);
        mrb.DefineMethod(bodyClass, mrb.Intern("each"u8), HttpBodyMembers.Each);
        mrb.DefineMethod(bodyClass, mrb.Intern("inspect"u8), HttpBodyMembers.Inspect);
    }

    /// <summary>Registers the Ruby <c>JSON</c> module (stdlib-compatible API), its error hierarchy, and <c>#to_json</c> on builtin types.</summary>
    /// <remarks>JSON numbers that fit Int64 become Integer; overflow falls back to Float. Encoding dispatches <c>obj.to_json</c> for non-builtin values.</remarks>
    public static void DefineJson(this MRubyState mrb)
    {
        var jsonModule = mrb.DefineModule(mrb.Intern("JSON"u8), mrb.ObjectClass);

        // NestingError < ParserError, matching CRuby's json.
        var jsonError = mrb.DefineClass(mrb.Intern("JSONError"u8), mrb.StandardErrorClass, outer: jsonModule);
        var parserError = mrb.DefineClass(mrb.Intern("ParserError"u8), jsonError, outer: jsonModule);
        mrb.DefineClass(mrb.Intern("GeneratorError"u8), jsonError, outer: jsonModule);
        mrb.DefineClass(mrb.Intern("NestingError"u8), parserError, outer: jsonModule);

        mrb.DefineClassMethod(jsonModule, mrb.Intern("parse"u8), JsonMembers.Parse);
        mrb.DefineClassMethod(jsonModule, mrb.Intern("generate"u8), JsonMembers.Generate);
        mrb.DefineClassMethod(jsonModule, mrb.Intern("pretty_generate"u8), JsonMembers.PrettyGenerate);
        mrb.DefineClassMethod(jsonModule, mrb.Intern("dump"u8), JsonMembers.Dump);
        mrb.DefineClassMethod(jsonModule, mrb.Intern("load"u8), JsonMembers.Load);

        var toJson = mrb.Intern("to_json"u8);
        mrb.DefineMethod(mrb.HashClass, toJson, JsonMembers.HashToJson);
        mrb.DefineMethod(mrb.ArrayClass, toJson, JsonMembers.ArrayToJson);
        mrb.DefineMethod(mrb.StringClass, toJson, JsonMembers.StringToJson);
        mrb.DefineMethod(mrb.IntegerClass, toJson, JsonMembers.IntegerToJson);
        mrb.DefineMethod(mrb.FloatClass, toJson, JsonMembers.FloatToJson);
        mrb.DefineMethod(mrb.TrueClass, toJson, JsonMembers.TrueToJson);
        mrb.DefineMethod(mrb.FalseClass, toJson, JsonMembers.FalseToJson);
        mrb.DefineMethod(mrb.NilClass, toJson, JsonMembers.NilToJson);
        mrb.DefineMethod(mrb.SymbolClass, toJson, JsonMembers.SymbolToJson);
    }
}
