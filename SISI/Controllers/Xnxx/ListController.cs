﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Lampac.Engine.CORE;
using Lampac.Models.SISI;
using Shared.Engine.SISI;
using Shared.Engine.CORE;
using SISI;

namespace Lampac.Controllers.Xnxx
{
    public class ListController : BaseSisiController
    {
        [HttpGet]
        [Route("xnx")]
        async public Task<ActionResult> Index(string search, int pg = 1)
        {
            var init = AppInit.conf.Xnxx.Clone();

            if (!init.enable)
                return OnError("disable");

            if (NoAccessGroup(init, out string error_msg))
                return OnError(error_msg, false);

            if (IsOverridehost(init, out string overridehost))
                return Redirect(overridehost);

            string memKey = $"xnx:list:{search}:{pg}";
            if (!hybridCache.TryGetValue(memKey, out List<PlaylistItem> playlists))
            {
                var proxyManager = new ProxyManager("xnx", init);
                var proxy = proxyManager.Get();

                reset: var rch = new RchClient(HttpContext, host, init, requestInfo);
                if (rch.IsNotSupport("web", out string rch_error))
                    return OnError(rch_error, false);

                if (rch.IsNotConnected())
                    return ContentTo(rch.connectionMsg);

                string html = await XnxxTo.InvokeHtml(init.corsHost(), search, pg, url =>
                    rch.enable ? rch.Get(init.cors(url), httpHeaders(init)) : HttpClient.Get(init.cors(url), timeoutSeconds: 10, proxy: proxy, headers: httpHeaders(init))
                );

                playlists = XnxxTo.Playlist($"{host}/xnx/vidosik", html);

                if (playlists.Count == 0)
                {
                    if (IsRhubFallback(init))
                        goto reset;

                    return OnError("playlists", proxyManager, !rch.enable && string.IsNullOrEmpty(search));
                }

                if (!rch.enable)
                    proxyManager.Success();

                hybridCache.Set(memKey, playlists, cacheTime(10));
            }

            return OnResult(playlists, string.IsNullOrEmpty(search) ? XnxxTo.Menu(host) : null, plugin: "xnx");
        }
    }
}
