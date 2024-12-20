﻿using Lampac.Models.LITE.KinoPub;
using Shared.Model.Online.KinoPub;
using Shared.Model.Templates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Shared.Engine.Online
{
    public class KinoPubInvoke
    {
        #region KinoPubInvoke
        string? host, token;
        string apihost;
        Func<string, ValueTask<string?>> onget;
        Func<string, string?, string> onstreamfile;
        Func<string, string>? onlog;
        Action? requesterror;

        public KinoPubInvoke(string? host, string apihost, string? token, Func<string, ValueTask<string?>> onget, Func<string, string?, string> onstreamfile, Func<string, string>? onlog = null, Action? requesterror = null)
        {
            this.host = host != null ? $"{host}/" : null;
            this.apihost = apihost;
            this.token = token;
            this.onget = onget;
            this.onstreamfile = onstreamfile;
            this.onlog = onlog;
            this.requesterror = requesterror;
        }
        #endregion

        #region Search
        /// <returns>
        /// 0 - Данные не получены
        /// 1 - Нет нужного контента 
        /// </returns>
        async public ValueTask<SearchResult?> Search(string? title, string? original_title, int year, int clarification, string? imdb_id, long kinopoisk_id)
        {
            if (string.IsNullOrEmpty(original_title) || string.IsNullOrEmpty(title))
                return null;

            async ValueTask<SearchResult?> goSearch(string q, bool sendrequesterror)
            {
                if (string.IsNullOrEmpty(q))
                    return null;

                string? json = await onget($"{apihost}/v1/items/search?q={HttpUtility.UrlEncode(q)}&access_token={token}&field=title&perpage=200");
                if (json == null)
                {
                    if (sendrequesterror)
                        requesterror?.Invoke();
                    return null;
                }

                try
                {
                    var items = JsonSerializer.Deserialize<SearchObject>(json)?.items;
                    if (items != null)
                    {
                        if (kinopoisk_id == 0 && string.IsNullOrEmpty(imdb_id))
                        {
                            var ids = new List<int>();
                            var stpl = new SimilarTpl(items.Count);

                            string? enc_title = HttpUtility.UrlEncode(title);
                            string? enc_original_title = HttpUtility.UrlEncode(original_title);

                            foreach (var item in items)
                            {
                                if (item.year == year || (item.year == year - 1) || (item.year == year + 1))
                                {
                                    stpl.Append(item.title, item.year.ToString(), item.voice, host + $"lite/kinopub?postid={item.id}&title={enc_title}&original_title={enc_original_title}");

                                    string _t = item.title.ToLower();
                                    string _s = q.ToLower();

                                    if (item.year == year && (_t.StartsWith(_s) || _t.EndsWith(_s)))
                                        ids.Add(item.id);
                                }
                            }

                            if (ids.Count == 1)
                                return new SearchResult() { id = ids[0] };

                            return new SearchResult() { similars = stpl };
                        }
                        else
                        {
                            foreach (var item in items)
                            {
                                if (item.kinopoisk > 0 && item.kinopoisk == kinopoisk_id)
                                    return new SearchResult() { id = item.id };

                                if ($"tt{item.imdb}" == imdb_id)
                                    return new SearchResult() { id = item.id };
                            }

                            return null;
                        }
                    }
                }
                catch { }

                return null;
            }

            if (clarification == 1)
            {
                if (string.IsNullOrEmpty(title))
                    return null;

                return await goSearch(title, true);
            }

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(original_title))
                return await goSearch(original_title ?? title, true);

            return (await goSearch(original_title, false)) ?? (await goSearch(title, true));
        }
        #endregion

        #region Post
        async public ValueTask<RootObject?> Post(int postid)
        {
            string? json = await onget($"{apihost}/v1/items/{postid}?access_token={token}");
            if (json == null)
            {
                requesterror?.Invoke();
                return null;
            }

            try
            {
                var root = JsonSerializer.Deserialize<RootObject>(json);
                if (root?.item?.seasons == null && root?.item?.videos == null)
                    return null;

                return root;
            }
            catch { return null; }
        }
        #endregion

        #region Html
        public string Html(RootObject? root, string? filetype, string? title, string? original_title, int postid, int s = -1, bool rjson = false)
        {
            if (root == null)
                return string.Empty;

            if (root?.item?.videos != null)
            {
                #region Фильм
                var mtpl = new MovieTpl(title, original_title, root.item.videos.Count);

                foreach (var v in root.item.videos)
                {
                    #region voicename
                    string voicename = string.Empty;

                    if (v.audios != null)
                    {
                        foreach (var audio in v.audios)
                        {
                            if (audio.lang == "eng")
                            {
                                if (!voicename.Contains(audio.lang))
                                    voicename += "eng, ";
                            }
                            else
                            {
                                string? a = audio?.author?.title ?? audio?.type?.title;
                                if (a != null)
                                {
                                    a = $"{a} ({audio.lang})";
                                    if (!voicename.Contains(a))
                                        voicename += $"{a}, ";
                                }
                            }
                        }

                        voicename = Regex.Replace(voicename, "[, ]+$", "");
                    }
                    #endregion

                    if (filetype == "hls4")
                    {
                        if (v.files[0].url.hls4 != null)
                            mtpl.Append(v.files[0].quality, onstreamfile(v.files[0].url.hls4, null), voice_name: voicename);
                    }
                    else
                    {
                        if (v.files[0].url.http == null)
                            continue;

                        #region subtitle
                        var subtitles = new SubtitleTpl();

                        if (v.subtitles != null)
                        {
                            foreach (var sub in v.subtitles)
                            {
                                if (sub.url != null)
                                    subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                            }
                        }
                        #endregion

                        var streamquality = new StreamQualityTpl(v.files.Select(f => (onstreamfile(f.url.http, f.file), f.quality)));
                        mtpl.Append(v.files[0].quality, onstreamfile(v.files[0].url.http, v.files[0].file), subtitles: subtitles, voice_name: voicename, streamquality: streamquality);
                    }
                }

                return rjson ? mtpl.ToJson() : mtpl.ToHtml();
                #endregion
            }
            else
            {
                if (root?.item?.seasons == null || root.item.seasons.Count == 0)
                    return string.Empty;

                #region Сериал
                string? enc_title = HttpUtility.UrlEncode(title);
                string? enc_original_title = HttpUtility.UrlEncode(original_title);

                if (s == -1)
                {
                    #region Сезоны
                    var tpl = new SeasonTpl(root.item.quality > 0 ? $"{root.item.quality}p" : null);

                    foreach (var season in root.item.seasons)
                    {
                        string link = host + $"lite/kinopub?rjson={rjson}&postid={postid}&title={enc_title}&original_title={enc_original_title}&s={season.number}";
                        tpl.Append($"{season.number} сезон", link, season.number);
                    }

                    return rjson ? tpl.ToJson() : tpl.ToHtml();
                    #endregion
                }
                else
                {
                    #region Серии
                    var etpl = new EpisodeTpl();

                    foreach (var episode in root.item.seasons.First(i => i.number == s).episodes)
                    {
                        #region voicename
                        string voicename = string.Empty;

                        if (episode.audios != null)
                        {
                            foreach (var audio in episode.audios)
                            {
                                string a = audio.author?.title ?? audio.lang;
                                if (a != null && !voicename.Contains(a) && a != "rus")
                                    voicename += $"{a}, ";
                            }

                            voicename = Regex.Replace(voicename, "[, ]+$", "");
                        }
                        #endregion

                        if (filetype == "hls4")
                        {
                            if (episode.files[0].url.hls4 == null)
                                continue;
                    
                            etpl.Append($"{episode.number} серия", title ?? original_title, s.ToString(), episode.number.ToString(), onstreamfile(episode.files[0].url.hls4, null), voice_name: voicename);
                        }
                        else
                        {
                            if (episode.files[0].url.http == null)
                                continue;

                            #region subtitle
                            var subtitles = new SubtitleTpl();

                            if (episode.subtitles != null)
                            {
                                foreach (var sub in episode.subtitles)
                                {
                                    if (sub.url != null)
                                        subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                                }
                            }
                            #endregion

                            #region streams
                            var streams = new List<(string link, string quality)>() { Capacity = episode.files.Count };

                            foreach (var f in episode.files)
                            {
                                if (f.url.http != null)
                                    streams.Add((onstreamfile(f.url.http, f.file), f.quality));
                            }
                            #endregion

                            string mp4 = onstreamfile(episode.files[0].url.http, episode.files[0].file);
                            etpl.Append($"{episode.number} серия", title ?? original_title, s.ToString(), episode.number.ToString(), mp4, subtitles: subtitles, voice_name: voicename, streamquality: new StreamQualityTpl(streams));
                        }
                    }

                    return rjson ? etpl.ToJson() : etpl.ToHtml();
                    #endregion
                }
                #endregion
            }
        }
        #endregion
    }
}
