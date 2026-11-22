using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace A2GPatch
{
    /// <summary>A question of the manager course quiz (TEXTE\fort.saq): its level, the question and the answers.</summary>
    internal sealed class QuizQuestion
    {
        /// <summary>The level (0 to 2), the order of the three blocks in the file.</summary>
        public int Level;
        /// <summary>The question.</summary>
        public string Question;
        /// <summary>The answers, separated by commas, the right one first; the game shows it and two of the others.</summary>
        public string Answers;
    }

    /// <summary>
    /// The quiz file TEXTE\fort.saq: the number of questions of each of the three levels, the number of texts
    /// (two per question), the length of the text block (all 32 bit), a table with where each text starts in
    /// the block, then the block: the texts one after another, each ended by a NUL, every byte inverted. The game
    /// reads the counts and sizes from the header and makes room for them (0x487800 in the 1999 exe), so
    /// questions may change length.
    /// </summary>
    internal static class QuizFile
    {
        private const int Levels = 3, HeaderSize = 4 * (Levels + 2);

        /// <summary>The questions of <paramref name="bytes"/> in their order; null if it isn't a quiz file.</summary>
        public static List<QuizQuestion> Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderSize)
                return null;
            long[] counts = Enumerable.Range(0, Levels + 2).Select(i => (long)BitConverter.ToUInt32(bytes, 4 * i)).ToArray();
            long questions = counts.Take(Levels).Sum(), tableEnd = HeaderSize + 4 * counts[Levels];
            if (counts[Levels] != 2 * questions || tableEnd + counts[Levels + 1] != bytes.Length)
                return null;
            byte[] plain = bytes.Skip((int)tableEnd).Select(b => (byte)~b).ToArray();
            string[] texts = TextPatch.Ansi.GetString(plain).Split('\0');
            if (texts.Length != counts[Levels] + 1 || texts[texts.Length - 1].Length != 0)
                return null;
            // Each text starts right behind the one before it.
            int at = 0;
            for (int i = 0; i < texts.Length - 1; i++)
            {
                if (BitConverter.ToUInt32(bytes, HeaderSize + 4 * i) != at)
                    return null;
                at += TextPatch.Ansi.GetByteCount(texts[i]) + 1;
            }
            List<QuizQuestion> result = new List<QuizQuestion>();
            for (int level = 0, next = 0; level < Levels; level++)
            {
                for (long n = 0; n < counts[level]; n++, next += 2)
                    result.Add(new QuizQuestion { Level = level, Question = texts[next], Answers = texts[next + 1] });
            }
            return result;
        }

        /// <summary>The quiz file holding <paramref name="questions"/>, each level's in their order.</summary>
        public static byte[] Write(IEnumerable<QuizQuestion> questions)
        {
            QuizQuestion[] sorted = questions.OrderBy(q => q.Level).ToArray();   // stable: the order within a level stays
            List<byte> header = new List<byte>(), block = new List<byte>();
            for (int level = 0; level < Levels; level++)
                header.AddRange(BitConverter.GetBytes(sorted.Count(q => q.Level == level)));
            header.AddRange(BitConverter.GetBytes(2 * sorted.Length));
            List<byte> table = new List<byte>();
            foreach (string text in sorted.SelectMany(q => new[] { q.Question, q.Answers }))
            {
                table.AddRange(BitConverter.GetBytes(block.Count));
                block.AddRange(TextPatch.Ansi.GetBytes(text + "\0"));
            }
            header.AddRange(BitConverter.GetBytes(block.Count));
            return header.Concat(table).Concat(block.Select(b => (byte)~b)).ToArray();
        }
    }

    /// <summary>
    /// One question of the quiz as the game has it (<see cref="From"/>) and as the patch makes it (<see cref="To"/>);
    /// a question the patch adds has no <see cref="From"/> and goes to the end of its <see cref="Level"/>.
    /// </summary>
    internal sealed class QuizChange
    {
        /// <summary>The question and its answers as the game has them (null for one the patch adds), and as changed.</summary>
        public readonly string[] From, To;
        /// <summary>For a question the patch adds: its level (0 to 2).</summary>
        public readonly int Level;

        private QuizChange(string[] from, string[] to, int level = 0)
        {
            this.From = from;
            this.To = to;
            this.Level = level;
        }

        /// <summary>The question <paramref name="question"/> with other answers.</summary>
        public static QuizChange Answers(string question, string from, string to) => new QuizChange(new[] { question, from }, new[] { question, to });
        /// <summary>The question rephrased; its answers stay.</summary>
        public static QuizChange Question(string from, string to, string answers) => new QuizChange(new[] { from, answers }, new[] { to, answers });
        /// <summary>The question rephrased, with other answers.</summary>
        public static QuizChange Both(string fromQuestion, string fromAnswers, string toQuestion, string toAnswers) =>
            new QuizChange(new[] { fromQuestion, fromAnswers }, new[] { toQuestion, toAnswers });
        /// <summary>A question the patch adds to <paramref name="level"/>.</summary>
        public static QuizChange New(int level, string question, string answers) => new QuizChange(null, new[] { question, answers }, level);

        // The question in questions that reads as text; null if none.
        private static QuizQuestion Find(IEnumerable<QuizQuestion> questions, string[] text) =>
            questions.FirstOrDefault(q => q.Question == text[0] && q.Answers == text[1]);

        /// <summary>Whether <paramref name="questions"/> are as the game has them (<paramref name="on"/> false) or as changed, as far as this change goes.</summary>
        public bool IsIn(List<QuizQuestion> questions, bool on) =>
            !on && this.From == null ? Find(questions, this.To) == null : Find(questions, on ? this.To : this.From) != null;

        /// <summary>Makes the change in <paramref name="questions"/> (<paramref name="on"/>) or takes it back; they are the other way round.</summary>
        public void Make(List<QuizQuestion> questions, bool on)
        {
            if (this.From == null && on)
                questions.Add(new QuizQuestion { Level = this.Level, Question = this.To[0], Answers = this.To[1] });
            else if (this.From == null)
                questions.Remove(Find(questions, this.To));
            else
            {
                QuizQuestion question = Find(questions, on ? this.From : this.To);
                string[] text = on ? this.To : this.From;
                question.Question = text[0];
                question.Answers = text[1];
            }
        }
    }

    /// <summary>
    /// A change to the manager course quiz (TEXTE\fort.saq), offered for every release. The switches change the
    /// file independently of each other; with none on it is the original to the byte.
    /// </summary>
    internal abstract class QuizPatch : FilePatch
    {
        /// <summary>The quiz file.</summary>
        internal static readonly string File = Path.Combine("TEXTE", "fort.saq");

        /// <inheritdoc/>
        public override PatchGroup Group => PatchGroup.Texts;
        /// <inheritdoc/>
        internal override bool NeedsExe => false;

        /// <summary>The changes.</summary>
        internal abstract QuizChange[] Changes { get; }

        /// <inheritdoc/>
        public override FileState State(GameFiles files)
        {
            List<QuizQuestion> questions = QuizFile.Read(files.Read(File));
            if (questions != null && this.Changes.All(c => c.IsIn(questions, true)))
                return FileState.On;
            return questions != null && this.Changes.All(c => c.IsIn(questions, false)) ? FileState.Off : FileState.Unknown;
        }

        /// <inheritdoc/>
        public override void Switch(GameFiles files, bool on)
        {
            List<QuizQuestion> questions = QuizFile.Read(files.Read(File));
            QuizChange[] open = this.Changes.Where(c => !c.IsIn(questions, on)).ToArray();
            foreach (QuizChange change in open)
                change.Make(questions, on);
            if (open.Length > 0)
                files.Write(File, QuizFile.Write(questions));
        }
    }

    /// <summary>
    /// The manager course quiz on today's state: answers that time has overtaken (titles, records, rules,
    /// people in office) or that were wrong already in 1998 (the UEFA's seat, Rangers' colours, a city with two
    /// clubs in the Bundesliga, the last double). Questions about a time that is over name their year. The
    /// questions keep their place and level.
    /// </summary>
    internal sealed class QuizAnswers : QuizPatch
    {
        /// <inheritdoc/>
        public override string Name => "Quiz der Managerfortbildung auf dem Stand von 2026: Titel, Rekorde, Regeln";
        /// <inheritdoc/>
        public override string Title => "Quizfragen";
        /// <inheritdoc/>
        internal override QuizChange[] Changes => List;

        /// <summary>The changes, in the order of the questions in the file.</summary>
        internal static readonly QuizChange[] List =
        {
            // Rules: the ball touching the referee, the goalkeeper's steps, coin tosses, kick-off and goal kick in play once kicked.
            QuizChange.Both("Ein Spieler schießt eine Ecke, die vom Schiedsrichter (aus Versehen) unhaltbar für den Torwart ins Tor geköpft wird. Entscheidung?", "Tor,Profivertrag,Abstoß,Wiederholung der Ecke",
                "Ein Spieler schießt eine Ecke, die vom Schiedsrichter (aus Versehen) unhaltbar für den Torwart ins Tor geköpft wird. Entscheidung (Stand November 2026)?", "Schiedsrichterball,Tor,Profivertrag,Wiederholung der Ecke"),
            QuizChange.Question("Wie heißt das größte Fußballstadion der Welt?", "Welches Stadion war bei seiner Eröffnung 1950 das größte Fußballstadion der Welt?",
                "Maracana,Ibrox Park,Zentralstadion,Estadio Kolumbia"),
            QuizChange.Both("Nach Abschluß einer Saison steigen aus der 1. Bundesliga in Deutschland wie viele Mannschaften ab?", "3,1,keine,2",
                "Wie viele Mannschaften stiegen im November 2026 nach einer Saison direkt aus der 1. Bundesliga ab?", "2 (der 16. muß in die Relegation),3,1,keine"),
            QuizChange.Both("Wie lautet der Stadionname von Schalke 04?", "Parkstadion,Gartenstadion,Ruhrpottstadion,Königsblaustadion",
                "Wie hieß das Stadion von Schalke 04 im November 2026?", "Veltins-Arena,Parkstadion,Ruhrpottstadion,Königsblaustadion"),
            QuizChange.Both("In welcher Stadt steht das größte deutsche Stadion?", "Leipzig,Berlin,München,Düsseldorf",
                "In welcher Stadt stand im November 2026 das größte deutsche Stadion?", "Dortmund,Leipzig,Berlin,München"),
            QuizChange.Both("Wie heißt der amtierende Präsident des Deutschen Fußballbundes?", "Egidius Braun,Gerhard Mayer-Vorfelder,Horst Hilpert,Franz Beckenbauer",
                "Wer war im November 2026 Präsident des Deutschen Fußballbundes?", "Bernd Neuendorf,Fritz Keller,Reinhard Grindel,Franz Beckenbauer"),
            QuizChange.Question("Helmut Kohl und Rudolf Scharping sind Fans welcher Mannschaft?", "Helmut Kohl und Rudolf Scharping galten als Fans welcher Mannschaft?",
                "1.FC Kaiserslautern,Bayern München,Waldhof Mannheim,1.FC Saarbrücken"),
            QuizChange.Question("Wie wird das finale Hallenturnier in der Winterpause bezeichnet?", "Wie hieß bis 2001 das finale Hallenturnier in der Winterpause?",
                "Hallen-Masters,Grand Slam Cup,Golden Four,Challenger Cup"),
            QuizChange.Both("Wie heißt der Manager von Bayern München?", "Uli Hoeneß,Felix Magath,Rainer Calmund,Willi Lemke",
                "Wer war im November 2026 Sportvorstand von Bayern München?", "Max Eberl,Uli Hoeneß,Rainer Calmund,Rudi Völler"),
            QuizChange.Question("Welcher aktuelle Bundesligaspieler trägt den Beinamen \"Tiger\"?", "Welcher Bundesligaspieler trug den Beinamen \"Tiger\"?",
                "Stefan Effenberg,Karl-Heinz Riedle,Bruno Labbadia,Fredi Bobic"),
            QuizChange.Both("Welcher Spieler hält mit 40 Toren in einer Saison den BL-Rekord?", "Gerd Müller,Klaus Allofs,Jupp Heynckes,K.-H. Rummenigge",
                "Welcher Spieler hielt im November 2026 mit 41 Toren in einer Saison den BL-Rekord?", "Robert Lewandowski,Gerd Müller,Harry Kane,K.-H. Rummenigge"),
            QuizChange.Both("Wie oft war Bayern München Deutscher Meister?", "14mal,Kann mich nicht erinnern.,Muß Jahre her sein.,Ist mir doch egal.",
                "Wie oft war Bayern München bis November 2026 Deutscher Meister?", "35mal,Kann mich nicht erinnern.,Muß Jahre her sein.,Ist mir doch egal."),
            QuizChange.Both("Wie viele Schritte darf ein Torwart mit dem Ball in der Hand machen?", "vier,keinen,fünf,unbegrenzt",
                "Wie viele Schritte darf ein Torwart mit dem Ball in der Hand machen (Stand November 2026)?", "unbegrenzt,vier,keinen,fünf"),
            QuizChange.Both("Der Schiedsrichter fälscht einen Schuß ab. Dieser führt zum Torerfolg. Entscheidung?", "reguläres Tor,kein Tor,Freistoß,Hochball",
                "Der Schiedsrichter fälscht einen Schuß ab. Dieser führt zum Torerfolg. Entscheidung (Stand November 2026)?", "Schiedsrichterball,reguläres Tor,Freistoß,Abstoß"),
            QuizChange.Both("Wie viele Mannschaften nehmen in Zukunft an der Endrunde einer Weltmeisterschaft teil?", "32,24,20,16",
                "Wie viele Mannschaften nahmen 2026 an der Endrunde der Weltmeisterschaft teil?", "48,32,40,64"),
            QuizChange.Question("Welcher dieser Herren ist kein FIFA-Schiedsrichter?", "Welcher dieser Herren war 1998 kein FIFA-Schiedsrichter?",
                "Michael Malbranc,Georg Dardenne,Edgar Steinborn,Bernd Heynemann"),
            QuizChange.Question("Welchem Beruf geht Schiedsrichter Markus Merk im täglichen Leben nach?", "Welchen Beruf übte Schiedsrichter Markus Merk neben dem Pfeifen aus?",
                "Zahnarzt,Lehrer,Sopransänger,Buchmacher"),
            QuizChange.Both("Welcher Spieler von Bayern München hat die meisten Europapokaleinsätze?", "Matthäus,Helmer,Kahn,Lizarazu",
                "Welcher Spieler hatte bis November 2026 die meisten Europapokaleinsätze für Bayern München?", "Müller,Matthäus,Kahn,Lahm"),
            QuizChange.Both("Welche Bedeutung hat es, wenn der Spielführer die Wahl vor Beginn eines Elfmeterschießens gewinnt?", "seine Mannschaft hat den ersten Schuß,die gegnerische Mannschaft hat den ersten Schuß,er bestimmt wer zuerst schießt,er darf sich ein Tor aussuchen (auf das dann geschossen wird)",
                "Welche Bedeutung hat es, wenn der Spielführer die Wahl vor Beginn eines Elfmeterschießens gewinnt (Stand November 2026)?", "er bestimmt wer zuerst schießt,seine Mannschaft hat den ersten Schuß,die gegnerische Mannschaft hat den ersten Schuß,er darf sich ein Tor aussuchen (auf das dann geschossen wird)"),
            QuizChange.Both("Was kann der Spielführer entscheiden, wenn er den Münzwurf zu Anfang des Spieles gewinnt?", "auf welcher Seite er zunächst spielen will,ob er den Anstoß hat,auf welcher Seite die Linienrichter stehen,mit welchem Ball gespielt wird",
                "Was kann der Spielführer entscheiden, wenn er den Münzwurf zu Anfang des Spieles gewinnt (Stand November 2026)?", "auf welcher Seite er zunächst spielen will oder ob er den Anstoß hat,nur ob er den Anstoß hat,auf welcher Seite die Linienrichter stehen,mit welchem Ball gespielt wird"),
            QuizChange.Question("Wie heißt der derzeit amtierende Chef des Ligaausschusses?", "Wer war 1998 Chef des Ligaausschusses?",
                "Gerhard Mayer-Vorfelder,Egidius Braun,Horst Hilpert,Franz Beckenbauer"),
            QuizChange.Both("Welcher Spieler absolvierte die meisten Länderspiele für Schweden?", "Ravelli,Hellström,Skoglund,Hamrin",
                "Welcher Spieler absolvierte bis November 2026 die meisten Länderspiele für Schweden?", "Anders Svensson,Ravelli,Hellström,Hamrin"),
            QuizChange.Question("Welcher Spieler wurde bisher wegen einer Schwalbe als einziger nachträglich gesperrt?",
                "Welcher Spieler wurde bis 1998 als einziger wegen einer Schwalbe nachträglich gesperrt?", "Andreas Möller,Thomas Häßler,Lothar Matthäus,Maurizio Gaudino"),
            QuizChange.Both("Wie heißt das größte deutsche Stadion?", "Zentralstadion,Olympiastadion,Sportpalast,Sachsenstadion",
                "Welches war im November 2026 das größte deutsche Stadion?", "Westfalenstadion (Signal Iduna Park),Olympiastadion Berlin,Allianz Arena,Sachsenstadion"),
            QuizChange.Question("Welcher Trainer war die längste Zeit ununterbrochen beim gleichen Bundesligaverein tätig?",
                "Welcher Trainer war bis 1998 die längste Zeit ununterbrochen beim gleichen Bundesligaverein tätig?",
                "Otto Rehhagel,Winfried Schäfer,Hennes Weisweiler,Christoph Daum"),
            // Cologne had two: 1. FC Köln and Fortuna Köln in 1973/74.
            QuizChange.Both("Welche der folgenden Städte hatte noch nie zwei Erstligisten in einer Saison?", "Köln,Stuttgart,München,Hamburg",
                "Welche dieser Städte hatte bis November 2026 noch nie zwei Vereine in derselben Bundesligasaison?", "Frankfurt,Stuttgart,München,Hamburg"),
            QuizChange.Both("Sepp Blatter ist der derzeit amtierende...?", "FIFA-Generalsekretär,FIFA-Präsident,Chef der Ideenkommission der FIFA,UEFA-Vorsitzende",
                "Welches Amt hatte Sepp Blatter, bevor er 1998 FIFA-Präsident wurde?", "FIFA-Generalsekretär,UEFA-Präsident,Chef der Ideenkommission der FIFA,Schweizer Nationaltrainer"),
            QuizChange.Both("Wie viele Vereine spielen derzeit in der Serie A in Italien?", "18,20,22,16",
                "Wie viele Vereine spielten im November 2026 in der Serie A in Italien?", "20,18,22,16"),
            QuizChange.Both("Wie oft wurde der VfB Stuttgart bislang Deutscher Meister?", "4mal,3mal,8mal,1mal",
                "Wie oft wurde der VfB Stuttgart bis November 2026 Deutscher Meister?", "5mal,4mal,3mal,8mal"),
            QuizChange.Both("Wie oft wurde der VfB Stuttgart bislang Deutscher Pokalsieger?", "3mal,4mal,8mal,1mal",
                "Wie oft wurde der VfB Stuttgart bis November 2026 Deutscher Pokalsieger?", "4mal,3mal,8mal,1mal"),
            QuizChange.Both("Wie oft wurde Borussia Dortmund bislang Deutscher Pokalsieger?", "2mal,4mal,3mal,1mal",
                "Wie oft wurde Borussia Dortmund bis November 2026 Deutscher Pokalsieger?", "5mal,2mal,4mal,3mal"),
            QuizChange.Both("Wie oft wurde der FC Schalke 04 bislang Deutscher Pokalsieger?", "2mal,4mal,3mal,1mal",
                "Wie oft wurde der FC Schalke 04 bis November 2026 Deutscher Pokalsieger?", "5mal,2mal,4mal,3mal"),
            QuizChange.Both("Welcher Spieler erzielte die meisten Tore bei Weltmeisterschaften?", "Müller,Maradona,Fontaine,Pele",
                "Welcher Spieler erzielte bis November 2026 die meisten Tore bei Weltmeisterschaften?", "Mbappé,Messi,Klose,Müller"),
            QuizChange.Question("Welcher Trainer einer Bundesligamannschaft arbeitet derzeit am längsten beim gleichen Verein?",
                "Welcher Trainer einer Bundesligamannschaft arbeitete 1998 am längsten beim gleichen Verein?",
                "Winfried Schäfer,Otto Rehhagel,Erich Ribbeck,Hannes Bongartz"),
            QuizChange.Question("Werder Bremen hatte in der Saison 1987/88 die wenigsten Gegentore kassiert (Bundesligarekord). Wie viele?",
                "Werder Bremen hatte in der Saison 1987/88 die wenigsten Gegentore kassiert (damals Bundesligarekord). Wie viele?",
                "22 Gegentreffer,12 Gegentreffer,9 Gegentreffer,28 Gegentreffer"),
            QuizChange.Answers("Wie viele Vereine waren seit Beginn der Bundesliga immer dabei?", "zwei,fünf,drei,acht", "keiner,zwei,fünf,drei"),
            QuizChange.Question("Welche Mannschaft der BL mußte bislang die meisten Platzverweise einstecken?", "Welche Mannschaft der BL mußte bis 1998 die meisten Platzverweise einstecken?",
                "1.FC Kaiserslautern,1.FC Köln,VfB Stuttgart,1.FC Nürnberg"),
            QuizChange.Question("Welcher Spieler der Bundesliga ist der körperlich größte?", "Welcher Spieler der Bundesliga war 1998 der körperlich größte?", "Golz,Gehrke,Brasas,Bäron"),
            QuizChange.Question("Wie viele Länder dürfen vier Mannschaften für den UEFA-Cup melden?", "Wie viele Länder durften 1997/98 vier Mannschaften für den UEFA-Cup melden?",
                "drei,acht,fünf,vier"),
            QuizChange.Question("Welcher Verein schoß in der Saison 1971/72 101 Tore (Bundesligarekord)?", "Welcher Verein schoß in der Saison 1971/72 101 Tore (damals Bundesligarekord)?",
                "Bayern München,Werder Bremen,Borussia Dortmund,Hamburger SV"),
            QuizChange.Both("Wie viele Jahre spielte Eintracht Braunschweig bislang in der Bundesliga?", "20 Jahre,10 Jahre,2 Jahre,Braunschweig? Nie.",
                "Wie viele Jahre spielte Eintracht Braunschweig bis November 2026 in der Bundesliga?", "21 Jahre,10 Jahre,2 Jahre,Braunschweig? Nie."),
            QuizChange.Both("Welche Mannschaft hält mit 36 Spielen in Folge ohne Niederlage den BL-Rekord?", "Hamburger SV,Bayern München,Borussia M'gladbach,1.FC Köln",
                "Welche Mannschaft hielt im November 2026 mit 53 Spielen in Folge ohne Niederlage den BL-Rekord?", "Bayern München,Hamburger SV,Bayer Leverkusen,Borussia M'gladbach"),
            QuizChange.Question("In welchem Jahr erreichte die Bundesliga mit mehr als 29.000 Zuschauern pro Spiel den besten Schnitt?",
                "In welchem Jahr erreichte die Bundesliga bis 1998 mit mehr als 29.000 Zuschauern pro Spiel den besten Schnitt?", "1995/96,1964/65,1977/78,1992/93"),
            QuizChange.Both("Wann gilt ein Ball nach einem Anstoß als \"im Spiel\"?", "wenn er sich in Richtung des gegnerischen Spielfeldes bewegt hat,nach der Berührung durch einen Spieler,nach der zweiten Berührung durch einen Spieler,nach dem Schiripfiff",
                "Wann gilt ein Ball nach einem Anstoß als \"im Spiel\" (Stand November 2026)?", "wenn er mit dem Fuß gespielt wurde und sich eindeutig bewegt,wenn er sich in Richtung des gegnerischen Spielfeldes bewegt hat,nach der zweiten Berührung durch einen Spieler,nach dem Schiripfiff"),
            QuizChange.Question("Wieviel bekommt ein Bundesligaschiedsrichter für die Leitung einer Partie?", "Wieviel bekam 1998 ein Bundesligaschiedsrichter für die Leitung einer Partie?",
                "4.000 DM,72 DM,2.500 DM,1.000 DM"),
            QuizChange.Both("Welcher Spieler erzielte nach Gerd Müller bislang die zweitmeisten Bundesligatore?", "Fischer,Heynckes,Burgsmüller,K. Allofs",
                "Welcher Spieler erzielte bis November 2026 nach Gerd Müller die zweitmeisten Bundesligatore?", "Lewandowski,Fischer,Heynckes,Burgsmüller"),
            QuizChange.Question("Wie heißt der schwerste Spieler bei Bayern München?", "Wer war 1998 der schwerste Spieler bei Bayern München?",
                "Carsten Jancker,Oliver Kahn,Bernd Dreher,Dennis Grassow"),
            QuizChange.Question("Wie heißt der größte Spieler von Borussia Dormund?", "Wer war 1998 der größte Spieler von Borussia Dortmund?",
                "Julio Cesar,Heiko Herrlich,Wolfgang de Beer,Rene Schneider"),
            QuizChange.Question("Wie heißt der Präsident des FC Schalke 04?", "Wer war 1998 Präsident des FC Schalke 04?", "Gerd Rehberg,Dr. Armin Langhorst,Peter Peters,Hubert Neu"),
            QuizChange.Question("Welche dieser Mannschaften war bisher nur eine Bundesligasaison nicht erstklassig?",
                "Welche dieser Mannschaften war bis 1998 nur eine Bundesligasaison nicht erstklassig?", "Werder Bremen,VfB Stuttgart,Eintracht Frankfurt,1.FC Köln"),
            QuizChange.Question("Welcher aktiver Spieler hat derzeit die meisten Bundesligastationen hinter sich?",
                "Welcher aktive Spieler hatte 1998 die meisten Bundesligastationen hinter sich?", "Stefan Kohn,Mirko Votava,Claus-Dieter Wollitz,Franco Foda"),
            QuizChange.Question("Wer gilt als der erfolgreichste Vereinstrainer der Welt?", "Wer galt 1998 als der erfolgreichste Vereinstrainer der Welt?",
                "Udo Lattek,Ernst Happel,Jupp Heynckes,Branco Zebec"),
            // The UEFA moved from Bern to Nyon in 1995.
            QuizChange.Both("In welcher Stadt hat die UEFA ihren Sitz?", "Bern,Brüssel,Paris,London",
                "In welcher Stadt hatte die UEFA im November 2026 ihren Sitz?", "Nyon,Bern,Brüssel,Paris"),
            // Bayern won the double in 1986 already, and last in 2026.
            QuizChange.Both("Welche Mannschaft gewann in Deutschland zuletzt Meisterschaft und Pokal in einer Saison?", "1.FC Köln,Hamburger SV,VfB Stuttgart,Bayern München",
                "Welche Mannschaft gewann bis November 2026 zuletzt Meisterschaft und Pokal in einer Saison?", "Bayern München,1.FC Köln,Hamburger SV,VfB Stuttgart"),
            QuizChange.Both("Welcher Schiedsrichter leitete bislang die meisten BL-Partien?", "Henning,Gabor,Eschweiler,Redelfs",
                "Welcher Schiedsrichter leitete bis November 2026 die meisten BL-Partien?", "Brych,Stark,Henning,Eschweiler"),
            QuizChange.Both("Was war der größte Erfolg Frankreichs bei einer WM?", "2mal Platz 3,Platz 4,Weltmeister 1962,Finale 1950",
                "Was war bis November 2026 der größte Erfolg Frankreichs bei einer WM?", "Weltmeister 1998 und 2018,2mal Platz 3,Weltmeister 1962,Finale 1950"),
            QuizChange.Both("Ein Spieler schießt den Ball beim Torabstoß direkt ins eigene Tor. Entscheidung?", "Wiederholung des Torabstoßes,Tor für die gegnerische Mannschaft,Freistoß für den Gegner,Gelbe Karte für den Spieler",
                "Ein Spieler schießt den Ball beim Torabstoß direkt ins eigene Tor. Entscheidung (Stand November 2026)?", "Eckstoß für den Gegner,Wiederholung des Torabstoßes,Tor für die gegnerische Mannschaft,Gelbe Karte für den Spieler"),
            QuizChange.Both("Welcher Torwart blieb am längsten ohne Gegentreffer (641 Minuten / Bundesligarekord)?", "Oliver Reck,Uli Stein,Toni Schumacher,Rüdiger Vollborn",
                "Welcher Torwart blieb am längsten ohne Gegentreffer (884 Minuten / Bundesligarekord, Stand November 2026)?", "Timo Hildebrand,Oliver Reck,Oliver Kahn,Manuel Neuer"),
            QuizChange.Both("Welcher Spieler hat bislang die meisten Tore in der zweiten Bundesliga erzielt?", "Schatzschneider,Mödrath,Cestonaro,Mill",
                "Welcher Spieler hat bis November 2026 die meisten Tore in der zweiten Bundesliga erzielt?", "Terodde,Schatzschneider,Mödrath,Mill"),
            QuizChange.Both("Welcher Spieler hat die meisten Zweitligaeinsätze in Deutschland absolviert?", "Montanes (Aachen),Schulz (Freiburg),Wulf (Kassel),Jurgeleit (Lübeck)",
                "Welcher Spieler hat bis November 2026 die meisten Zweitligaeinsätze in Deutschland absolviert?", "Landgraf (Aachen),Montanes (Aachen),Schulz (Freiburg),Wulf (Kassel)"),
            QuizChange.Both("Welche Mannschaft verteidigte zuletzt den DFB-Pokal erfolgreich?", "Fortuna Düsseldorf,Bayern München,1.FC Köln,Borussia Dortmund",
                "Welche Mannschaft verteidigte bis November 2026 zuletzt den DFB-Pokal erfolgreich?", "RB Leipzig,Fortuna Düsseldorf,Bayern München,1.FC Köln"),
            QuizChange.Question("Wie viele Vereine gewannen bisher alle drei Europapokalwettbewerbe?", "Wie viele Vereine gewannen bis 1998 alle drei Europapokalwettbewerbe?", "4,3,6,2"),
            QuizChange.Both("Welcher Mannschaft gelang zuletzt die Verteidigung des UEFA-Pokals?", "Real Madrid,FC Barcelona,Ipswich Town,SSC Neapel",
                "Welcher Mannschaft gelang bis November 2026 zuletzt die Verteidigung des UEFA-Pokals?", "FC Sevilla,Real Madrid,Ipswich Town,SSC Neapel"),
            QuizChange.Both("Wie oft war Manchester United englischer Meister?", "11mal,5mal,20mal,8 mal",
                "Wie oft war Manchester United bis November 2026 englischer Meister?", "20mal,11mal,5mal,8mal"),
            QuizChange.Both("Wie oft war Paris St. Germain französischer Meister?", "3mal,20mal,7mal,9 mal",
                "Wie oft war Paris St. Germain bis November 2026 französischer Meister?", "14mal,3mal,7mal,9mal"),
            QuizChange.Question("Welcher Dortmunder Spieler hat die meisten Europapokaleinsätze?", "Welcher Dortmunder Spieler hatte 1998 die meisten Europapokaleinsätze?",
                "Reuter,Kohler,Zorc,Möller"),
            QuizChange.Question("Welcher Spieler des VfB Stuttgart hat die meisten Europapokaleinsätze?", "Welcher Spieler des VfB Stuttgart hatte 1998 die meisten Europapokaleinsätze?",
                "Wohlfahrt,Verlaat,Poschner,Berthold"),
            QuizChange.Question("Welche dieser Mannschaften nahm bislang am häufigsten an einem Europapokal teil?",
                "Welche dieser Mannschaften nahm bis 1998 am häufigsten an einem Europapokal teil?", "CF Barcelona,Real Madrid,Bayern München,Juventus Turin"),
            // Rangers play in blue.
            QuizChange.Answers("Welches sind die Vereinsfarben der Glasgow Rangers?", "rot/weiß,grün/weiß,schwarz/weiß,blau/weiß", "blau/weiß/rot,grün/weiß,schwarz/weiß,gelb/schwarz"),
            QuizChange.Question("Welchen Verein trainierte der heutige HSV-Trainer Frank Pagelsdorf vor seiner Zeit bei Hansa Rostock?",
                "Welchen Verein trainierte Frank Pagelsdorf vor seiner Zeit bei Hansa Rostock?", "1.FC Union Berlin,VfL Bochum,Arminia Bielefeld,Fortuna Köln"),
            QuizChange.Question("Bei welchem Bundesligaverein ist Claus Boden Torwarttrainer?", "Bei welchem Bundesligaverein war Claus Boden 1998 Torwarttrainer?",
                "TSV 1860 München,1.FC Kaiserslautern,MSV Duisburg,1.FC Köln"),
            QuizChange.Question("Wie heißt der zur Zeit jüngste Bundesligaschiedsrichter?", "Wer war 1998 der jüngste Bundesligaschiedsrichter?",
                "Wolfgang Stark,Torsten Koop,Bernhard Zerr,Herbert Fandel"),
            QuizChange.Question("Wie heißt der DFB-Schiedsrichterobmann?", "Wer war 1998 DFB-Schiedsrichterobmann?", "Volker Roth,Walter Eschweiler,Johannes Malka,Egidius Braun"),
            QuizChange.Question("Welcher deutscher Schiedsrichter hat mit vier Spielen die meisten WM-Spiele gepfiffen?",
                "Welcher deutsche Schiedsrichter hatte bis 1998 mit vier Spielen die meisten WM-Spiele gepfiffen?", "Siegfried Kirschen,Albert Dusch,Kurt Tschenscher,Volker Roth"),
            QuizChange.Question("Welche Autofirma sponsort den Weltcup?", "Welche Autofirma sponserte bis 2004 den Weltpokal der Vereinsmannschaften?", "Toyota,Mazda,Honda,Mitsubishi"),
            QuizChange.Question("Welche Bundesligamannschaft belegte nach der Herbstmeisterschaft bisher am Saisonende den schlechtesten Tabellenplatz (5.)?",
                "Welche Bundesligamannschaft belegte bis 1998 nach der Herbstmeisterschaft am Saisonende den schlechtesten Tabellenplatz (5.)?",
                "Eintracht Frankfurt,Bayern München,Borussia Dortmund,Werder Bremen"),
            QuizChange.Question("Welche Mannschaft gewann bisher als einzige drei Herbstmeisterschaften in Serie?",
                "Welche Mannschaft gewann bis 1998 als einzige drei Herbstmeisterschaften in Serie?", "Borussia Mönchengladbach,1.FC Kaiserslautern,Bayern München,Borussia Dortmund"),
        };
    }
}
