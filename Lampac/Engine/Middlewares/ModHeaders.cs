﻿using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lampac.Engine.Middlewares
{
    public class ModHeaders
    {
        private readonly RequestDelegate _next;
        public ModHeaders(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            httpContext.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
            httpContext.Response.Headers.Add("Access-Control-Allow-Private-Network", "true");
            httpContext.Response.Headers.Add("Access-Control-Allow-Headers", "Accept, Origin, Content-Type");
            httpContext.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");

            if (httpContext.Request.Headers.TryGetValue("origin", out var origin))
                httpContext.Response.Headers.Add("Access-Control-Allow-Origin", origin.ToString());
            else if (httpContext.Request.Headers.TryGetValue("referer", out var referer))
                httpContext.Response.Headers.Add("Access-Control-Allow-Origin", referer.ToString());
            else
                httpContext.Response.Headers.Add("Access-Control-Allow-Origin", "*");

            if (Regex.IsMatch(httpContext.Request.Path.Value, "^/(lampainit|sisi|lite|online|tmdbproxy|tracks|dlna)\\.js"))
            {
                httpContext.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate"); // HTTP 1.1.
                httpContext.Response.Headers.Add("Pragma", "no-cache"); // HTTP 1.0.
                httpContext.Response.Headers.Add("Expires", "0"); // Proxies.
            }

            if (HttpMethods.IsOptions(httpContext.Request.Method))
                return Task.CompletedTask;

            return _next(httpContext);
        }
    }
}
