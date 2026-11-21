namespace A2GPatch
{
    /// <summary>
    /// Euro instead of Deutsche Mark: the money formats (strings 2162-2164, which every amount the game shows
    /// goes through), the amounts the dialogs show until the game fills in its own, the money in four event
    /// texts and in the tips. The numbers stay: 1 DM is shown as 1 €. "DM" alone is the defensive midfielder
    /// (string 1147 and three dialogs) and stays. The event texts keep their length with a space at the end.
    /// </summary>
    internal sealed class Currency : TextPatch
    {
        /// <inheritdoc/>
        public override string Name => "Beträge in € statt DM, auch in Ereignissen und Tipps; die Zahlen bleiben gleich";
        /// <inheritdoc/>
        public override string Title => "Euro statt DM";

        /// <inheritdoc/>
        internal override TextChange[] Changes { get; } =
        {
            TextChange.Strings(" DM", " €", 2162, 2163, 2164),
            TextChange.Strings("eine weitere Mark", "einen weiteren Euro", 1764),
            TextChange.Dialogs(" DM", " €", 116, 131, 132, 136, 193, 198, 199, 207, 208, 209, 210, 212, 213, 214, 215, 216, 217, 220,
                234, 236, 237, 238, 239, 241, 242, 243, 244, 245, 248, 259, 263, 265, 270, 271, 276, 279, 281, 283, 286, 293, 294, 299,
                311, 337, 376, 377, 378, 389, 403, 434, 440, 452, 474, 482, 492, 510, 513, 516, 522, 528, 533, 546, 547, 549, 559, 570,
                577, 578, 581, 582, 583, 588, 589, 593, 595, 605, 612, 620, 628),
            TextChange.Event(229,
                "Ein Wettbüro bietet Ihrem Verein %s DM für eine Niederlage im nächsten Spiel gegen %s.",
                "Ein Wettbüro bietet Ihrem Verein %s € für eine Niederlage im nächsten Spiel gegen %s. "),
            TextChange.Event(258,
                "Ein Medizinmann aus Afrika bietet Ihnen gegen eine einmalige Gebühr von 300.000 DM an, alle Formwerte Ihrer Mannschaft um fünf Punkte zu erhöhen. Nehmen Sie an?",
                "Ein Medizinmann aus Afrika bietet Ihnen gegen eine einmalige Gebühr von 300.000 € an, alle Formwerte Ihrer Mannschaft um fünf Punkte zu erhöhen. Nehmen Sie an? "),
            TextChange.Event(286,
                "Die Existenz Ihrer schwarzen Kasse ist leider den Finanzbehörden zu Ohren gekommen. Sie werden zu einer Strafe von 200.000 DM verdonnert. Außerdem muß das Geld sofort versteuert werden, so daß weitere %s abgeführt werden müssen. Der Rest wandert direkt auf das Vereinskonto.",
                "Die Existenz Ihrer schwarzen Kasse ist leider den Finanzbehörden zu Ohren gekommen. Sie werden zu einer Strafe von 200.000 € verdonnert. Außerdem muß das Geld sofort versteuert werden, so daß weitere %s abgeführt werden müssen. Der Rest wandert direkt auf das Vereinskonto. "),
            TextChange.Event(330,
                "Ihr Spieler %s fordert eine Sonderprämie in Höhe von 100.000 DM, da er ein lukratives Auslandsangebot vorliegen habe. Sind Sie einverstanden?",
                "Ihr Spieler %s fordert eine Sonderprämie in Höhe von 100.000 €, da er ein lukratives Auslandsangebot vorliegen habe. Sind Sie einverstanden? "),
            TextChange.Event(501,
                "Weil Sie unbedingt noch die Anstoß 2-Partie vom Abend zuvor fertig spielen mußten, erscheinen Sie zu spät zum Training und müssen 1.000 Mark in die Mannschaftskasse zahlen.",
                "Weil Sie unbedingt noch die Anstoß 2-Partie vom Abend zuvor fertig spielen mußten, erscheinen Sie zu spät zum Training und müssen 1.000 € in die Mannschaftskasse zahlen.   "),
            TextChange.Tips(" DM", " €"),
        };
    }

    /// <summary>
    /// Centre-back instead of man-marker: the position's name and abbreviation in the strings and dialogs, and
    /// in the event about a player who learned a new main position. That event's text is rephrased six bytes
    /// shorter, as the longer name needs them.
    /// </summary>
    internal sealed class CentreBack : TextPatch
    {
        /// <inheritdoc/>
        public override string Name => "Innenverteidiger (IV) statt Manndecker (MD)";
        /// <inheritdoc/>
        public override string Title => "Innenverteidiger";

        /// <inheritdoc/>
        internal override TextChange[] Changes { get; } =
        {
            TextChange.Strings("MD", "IV", 1144),
            TextChange.Strings("Manndecker", "Innenverteidiger", 1174, 1370),
            TextChange.Dialogs("MD", "IV", 200, 396, 609),
            TextChange.Dialogs("Manndecker", "Innenverteidiger", 291),
            TextChange.Event(906,
                "Ihr Spieler %s hat eine neue Hauptposition gelernt, er ist nun %s.",
                "Ihr Spieler %s hat eine neue Hauptposition gelernt: %s.     "),
            TextChange.Event(909, "Manndecker", "Innenverteidiger"),
        };
    }

    /// <summary>The European cups by their later names: UEFA Cup, Club World Cup as "Weltcup", Conference League for the Intertoto Cup.</summary>
    internal sealed class CupNames : TextPatch
    {
        /// <inheritdoc/>
        public override string Name => "UEFA-Cup, Cup der Pokalsieger, Weltcup und ECL-Cup statt UEFA-Pokal, Pokal der Pokalsieger, Weltpokal und UI-Cup";
        /// <inheritdoc/>
        public override string Title => "Europapokale";

        /// <inheritdoc/>
        internal override TextChange[] Changes { get; } =
        {
            TextChange.Strings("Pokal der Pokalsieger", "Cup der Pokalsieger", 1556, 1561, 1869, 2032, 2488, 2497, 2500, 2503, 5390, 5648),
            TextChange.Strings("Pokals der Pokalsieger", "Cups der Pokalsieger", 2643),
            TextChange.Dialogs("Pokal der Pokalsieger", "Cup der Pokalsieger", 509, 626),
            TextChange.Dialogs("Europapokal der Pokalsieger", "Cup der Pokalsieger", 274),
            TextChange.Strings("UEFA-Pokal", "UEFA-Cup", 1557, 1562, 1870, 2033, 2489, 2498, 2501, 2504, 5391, 5649, 5761, 6282),
            TextChange.Strings("UEFA-Pokalplatz", "UEFA-Cup-Platz", 1077),
            TextChange.Strings("UEFA - Pokals", "UEFA-Cups", 2642),
            TextChange.Dialogs("UEFA-Pokal", "UEFA-Cup", 241, 270, 271, 281, 626, 629),
            TextChange.Strings("Weltpokal", "Weltcup", 2490, 5653, 5763, 5970),
            TextChange.Dialogs("Weltpokal", "Weltcup", 626),
            TextChange.Strings("UI-Cup", "ECL-Cup", 1558, 1879, 2034, 2492, 5393, 5655, 6352),
            TextChange.Strings("UIC", "ECL", 5972),
            TextChange.Dialogs("UI-Cup", "ECL-Cup", 274),
            TextChange.Dialogs("UIC", "ECL", 274),
        };
    }

    /// <summary>
    /// The leagues by their current names. The Regionalliga Nordost keeps its name, which it has again
    /// since 2012; West/Südwest became Regionalliga West.
    /// </summary>
    internal sealed class LeagueNames : TextPatch
    {
        /// <inheritdoc/>
        public override string Name => "Heutige Ligennamen: 1. und 2. Bundesliga, Regionalliga West, Championship, League One, Ligue 1 und 2";
        /// <inheritdoc/>
        public override string Title => "Liganamen";

        /// <inheritdoc/>
        internal override TextChange[] Changes { get; } =
        {
            TextChange.Strings("Bundesliga", "1. Bundesliga", 1019),
            TextChange.Strings("2. Liga", "2. Bundesliga", 1020),
            TextChange.Strings("2. Liga", "2. B.liga", 5584),
            TextChange.Strings("Regionalliga West/Südwest", "Regionalliga West", 1024),
            TextChange.Strings("RL W./SW..", "RL West", 5588),
            TextChange.Dialogs("Regionalliga West-Südwest", "Regionalliga West", 275),
            TextChange.Dialogs("West-Südwest", "West", 189),
            TextChange.Strings("Division One", "Championship", 1026),
            TextChange.Strings("Division Two", "League One", 1027),
            TextChange.Strings("Division 1", "Champions.", 5590),
            TextChange.Strings("Division 2", "League One", 5591),
            TextChange.Strings("Division 1", "Ligue 1", 1028, 5592),
            TextChange.Strings("Division 2", "Ligue 2", 1029, 5593),
            TextChange.Strings("1a Division", "Primera D.", 5595),
        };
    }

    /// <summary>The groups of the cups and championships with letters, as UEFA and FIFA name them.</summary>
    internal sealed class GroupLetters : TextPatch
    {
        /// <inheritdoc/>
        public override string Name => "Gruppen A bis H statt 1 bis 8";
        /// <inheritdoc/>
        public override string Title => "Gruppen A bis H";

        /// <inheritdoc/>
        internal override TextChange[] Changes { get; } =
        {
            TextChange.Strings("Gruppe 1", "Gruppe A", 6032),
            TextChange.Strings("Gruppe 2", "Gruppe B", 6033),
            TextChange.Strings("Gruppe 3", "Gruppe C", 6034),
            TextChange.Strings("Gruppe 4", "Gruppe D", 6035),
            TextChange.Strings("Gruppe 5", "Gruppe E", 6036),
            TextChange.Strings("Gruppe 6", "Gruppe F", 6037),
            TextChange.Strings("Gruppe 7", "Gruppe G", 6038),
            TextChange.Strings("Gruppe 8", "Gruppe H", 6039),
            TextChange.Strings("Gruppen 1/2", "Gruppen A/B", 2132),
            TextChange.Strings("Gruppen 3/4", "Gruppen C/D", 2133),
            TextChange.Strings("Gruppen 5/6", "Gruppen E/F", 2134),
            TextChange.Strings("Gruppen 7/8", "Gruppen G/H", 6052),
        };
    }

    /// <summary>
    /// Contract terms as they are used today: base salary for fixed salary, release clause for the
    /// manager's "Freigabeklausel", a player's agreed fixed transfer fee and the exit on an offer, record
    /// transfers for super transfers.
    /// </summary>
    internal sealed class ContractTerms : TextPatch
    {
        /// <inheritdoc/>
        public override string Name => "Grundgehalt statt Festgehalt, Ausstiegsklausel statt Freigabeklausel und fixer Ablöse, Rekordtransfers statt Supertransfers";
        /// <inheritdoc/>
        public override string Title => "Vertragsbegriffe";

        /// <inheritdoc/>
        internal override TextChange[] Changes { get; } =
        {
            TextChange.Strings("Festgehalt", "Grundgehalt", 2658),
            TextChange.Dialogs("Festgehalt", "Grundgehalt", 209, 210, 279, 293, 294, 311, 389, 490, 612),
            TextChange.Dialogs("Freigabeklausel", "Ausstiegsklausel", 221, 401, 402),
            TextChange.Dialogs("Fixe Ablöse vereinbart", "Ausstiegsklausel", 293, 294, 311),
            TextChange.Strings("Fixe Ablöse", "Ausstiegsklausel", 2410),
            TextChange.Dialogs("Fixe Ablöse", "Ausstiegsklausel", 200, 568),
            TextChange.Strings("Ausstieg bei Angebot möglich", "Ausstiegsklausel", 2664),
            TextChange.Dialogs("Ausstieg bei Angebot möglich", "Ausstiegsklausel", 279),
            TextChange.Dialogs("Ausstieg bei Angebot", "Ausstiegsklausel", 210),
            TextChange.Strings("Supertransfers", "Rekordtransfers", 2153),
        };
    }

    /// <summary>
    /// Typing errors in the game's texts, one of the game's bugs: the position names 911-913 end with a full
    /// stop although the sentence they go into has its own ("rechter Verteidiger.."), a doubled "mehr", a
    /// stray comma and full stop in the player descriptions, missing full stops, "das" for "daß", a
    /// truncated and a cramped Supercup heading. The event texts keep their length with spaces after the
    /// stadium message.
    /// </summary>
    internal sealed class TextTypos : TextPatch
    {
        /// <inheritdoc/>
        public override string Name => "Satzzeichen und Schreibfehler in Texten des Spiels (z. B. „rechter Verteidiger..“)";
        /// <inheritdoc/>
        public override string Title => "Tippfehler";
        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Patches;

        /// <inheritdoc/>
        internal override TextChange[] Changes { get; } =
        {
            TextChange.Strings(", und er ist", "und er ist", 1201),
            TextChange.Strings("harter Spieler.", "harter Spieler", 1333),
            TextChange.Strings("Spiele gewonnen", "Spiele gewonnen.", 1120),
            TextChange.Strings("sehr zufrieden", "sehr zufrieden.", 1750, 1751),
            TextChange.Strings("Meinung, das es", "Meinung, daß es", 1754),
            TextChange.Strings("des Nationalen Pokals", "des nationalen Pokals", 2641),
            TextChange.Strings("UEFA - Pokals", "UEFA-Pokals", 2642),
            TextChange.Strings("Europäischer Superc.", "Europ. Supercup", 2035),
            TextChange.Strings("E.Supercup", "E. Supercup", 5762),
            TextChange.Event(911, "rechter Verteidiger.", "rechter Verteidiger"),
            TextChange.Event(912, "rechter Mittelfeldspieler.", "rechter Mittelfeldspieler"),
            TextChange.Event(913, "linker Mittelfeldspieler.", "linker Mittelfeldspieler"),
            TextChange.Event(899,
                "Ihr Verein teilt Ihnen mit, daß ab dem Saisonende kein Interesse mehr an einer weiteren Zusammenarbeit mehr besteht. Es müßten neue Impulse kommen, um den Verein in eine gute Zukunft zu steuern. ",
                "Ihr Verein teilt Ihnen mit, daß ab dem Saisonende kein Interesse mehr an einer weiteren Zusammenarbeit besteht. Es müßten neue Impulse kommen, um den Verein in eine gute Zukunft zu steuern. "),
            TextChange.Event(905, "Ein Teil Ihres Stadions wurde fertiggestellt.", "Ein Teil Ihres Stadions wurde fertiggestellt.        "),
        };
    }
}
