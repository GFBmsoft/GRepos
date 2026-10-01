using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GRepos.Services;

/// <summary>
/// Códigos de emoji do GitHub (":hammer:") trocados pelo caractere. A lista cobre os
/// que aparecem em README e em commit no padrão gitmoji; código desconhecido fica como
/// está, que é o mesmo que o GitHub faz.
/// </summary>
public static class Emojis
{
    private static readonly Regex Codigo = new(@":([a-z0-9_+\-]+):", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Mapa = new()
    {
        ["bookmark_tabs"] = "📑", ["bookmark"] = "🔖", ["hammer"] = "🔨", ["wrench"] = "🔧",
        ["hammer_and_wrench"] = "🛠️", ["gear"] = "⚙️", ["package"] = "📦", ["rocket"] = "🚀",
        ["sparkles"] = "✨", ["bug"] = "🐛", ["fire"] = "🔥", ["memo"] = "📝", ["pencil"] = "📝",
        ["pencil2"] = "✏️", ["books"] = "📚", ["book"] = "📖", ["open_book"] = "📖",
        ["art"] = "🎨", ["zap"] = "⚡", ["lock"] = "🔒", ["unlock"] = "🔓", ["key"] = "🔑",
        ["white_check_mark"] = "✅", ["heavy_check_mark"] = "✔️", ["x"] = "❌", ["warning"] = "⚠️",
        ["construction"] = "🚧", ["rotating_light"] = "🚨", ["green_heart"] = "💚", ["heart"] = "❤️",
        ["arrow_up"] = "⬆️", ["arrow_down"] = "⬇️", ["arrow_right"] = "➡️", ["arrow_left"] = "⬅️",
        ["pushpin"] = "📌", ["round_pushpin"] = "📍", ["recycle"] = "♻️", ["heavy_plus_sign"] = "➕",
        ["heavy_minus_sign"] = "➖", ["truck"] = "🚚", ["page_facing_up"] = "📄", ["boom"] = "💥",
        ["bento"] = "🍱", ["wheelchair"] = "♿", ["bulb"] = "💡", ["beers"] = "🍻",
        ["speech_balloon"] = "💬", ["card_file_box"] = "🗃️", ["loud_sound"] = "🔊", ["mute"] = "🔇",
        ["busts_in_silhouette"] = "👥", ["bust_in_silhouette"] = "👤", ["children_crossing"] = "🚸",
        ["building_construction"] = "🏗️", ["iphone"] = "📱", ["clown_face"] = "🤡", ["egg"] = "🥚",
        ["see_no_evil"] = "🙈", ["camera_flash"] = "📸", ["alembic"] = "⚗️", ["mag"] = "🔍",
        ["label"] = "🏷️", ["seedling"] = "🌱", ["triangular_flag_on_post"] = "🚩",
        ["goal_net"] = "🥅", ["dizzy"] = "💫", ["wastebasket"] = "🗑️", ["passport_control"] = "🛂",
        ["adhesive_bandage"] = "🩹", ["monocle_face"] = "🧐", ["coffin"] = "⚰️", ["test_tube"] = "🧪",
        ["necktie"] = "👔", ["stethoscope"] = "🩺", ["bricks"] = "🧱", ["technologist"] = "🧑‍💻",
        ["money_with_wings"] = "💸", ["thread"] = "🧵", ["safety_vest"] = "🦺", ["airplane"] = "✈️",
        ["ambulance"] = "🚑", ["lipstick"] = "💄", ["tada"] = "🎉", ["globe_with_meridians"] = "🌐",
        ["computer"] = "💻", ["floppy_disk"] = "💾", ["file_folder"] = "📁", ["open_file_folder"] = "📂",
        ["clipboard"] = "📋", ["calendar"] = "📆", ["date"] = "📅", ["chart_with_upwards_trend"] = "📈",
        ["bar_chart"] = "📊", ["link"] = "🔗", ["paperclip"] = "📎", ["email"] = "📧",
        ["envelope"] = "✉️", ["inbox_tray"] = "📥", ["outbox_tray"] = "📤", ["moneybag"] = "💰",
        ["dollar"] = "💵", ["credit_card"] = "💳", ["receipt"] = "🧾", ["bank"] = "🏦",
        ["information_source"] = "ℹ️", ["question"] = "❓", ["exclamation"] = "❗", ["star"] = "⭐",
        ["star2"] = "🌟", ["eyes"] = "👀", ["point_right"] = "👉", ["+1"] = "👍", ["thumbsup"] = "👍",
        ["-1"] = "👎", ["thumbsdown"] = "👎", ["clap"] = "👏", ["muscle"] = "💪", ["pray"] = "🙏",
        ["smile"] = "😄", ["smiley"] = "😃", ["wink"] = "😉", ["joy"] = "😂", ["thinking"] = "🤔",
        ["hourglass"] = "⌛", ["stopwatch"] = "⏱️", ["alarm_clock"] = "⏰", ["dart"] = "🎯",
        ["trophy"] = "🏆", ["medal_sports"] = "🏅", ["checkered_flag"] = "🏁", ["no_entry"] = "⛔",
        ["no_entry_sign"] = "🚫", ["shield"] = "🛡️", ["satellite"] = "📡", ["electric_plug"] = "🔌",
        ["battery"] = "🔋", ["desktop_computer"] = "🖥️", ["keyboard"] = "⌨️", ["printer"] = "🖨️",
        ["toolbox"] = "🧰", ["jigsaw"] = "🧩", ["dna"] = "🧬", ["microscope"] = "🔬",
        ["telescope"] = "🔭", ["scroll"] = "📜", ["newspaper"] = "📰", ["notebook"] = "📓",
        ["ledger"] = "📒", ["card_index"] = "📇", ["page_with_curl"] = "📃", ["spiral_notepad"] = "🗒️",
        ["triangular_ruler"] = "📐", ["straight_ruler"] = "📏", ["scissors"] = "✂️", ["nut_and_bolt"] = "🔩",
        ["new"] = "🆕", ["up"] = "🆙", ["cool"] = "🆒", ["free"] = "🆓",
        ["sos"] = "🆘", ["100"] = "💯", ["heavy_exclamation_mark"] = "❗", ["grey_question"] = "❔",
        ["white_circle"] = "⚪", ["red_circle"] = "🔴", ["large_blue_circle"] = "🔵",
        ["green_circle"] = "🟢", ["yellow_circle"] = "🟡", ["brazil"] = "🇧🇷",
    };

    /// <summary>Troca os códigos conhecidos; o resto do texto não muda.</summary>
    public static string Trocar(string texto)
    {
        if (string.IsNullOrEmpty(texto) || texto.IndexOf(':') < 0) return texto;
        return Codigo.Replace(texto, m => Mapa.TryGetValue(m.Groups[1].Value, out var e) ? e : m.Value);
    }
}
