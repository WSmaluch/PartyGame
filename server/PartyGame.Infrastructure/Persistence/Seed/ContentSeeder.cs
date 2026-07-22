using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.GameEngine;

namespace PartyGame.Infrastructure.Persistence.Seed;

public static class ContentSeeder
{
    public static readonly Guid StarterLogicalPackageId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task SeedAsync(PartyGameDbContext dbContext, IGameClock clock)
    {
        var now = clock.UtcNow;

        var existingPack = await dbContext.GamePackages
            .FirstOrDefaultAsync(p => p.LogicalPackageId == StarterLogicalPackageId || p.Key == "starter");

        if (existingPack is null)
        {
            var pack = new GamePackage
            {
                Id = StarterLogicalPackageId,
                LogicalPackageId = StarterLogicalPackageId,
                Version = 1,
                Key = "starter",
                NamePl = "Pakiet startowy",
                NameEn = "Starter Pack",
                DescriptionPl = "Podstawowy pakiet pytań imprezowych.",
                DescriptionEn = "Basic party game questions pack.",
                Status = ContentPackageStatus.Published,
                IsActive = true,
                IsDefault = true,
                SortOrder = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                PublishedAtUtc = now,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            };
            dbContext.GamePackages.Add(pack);

            AddCategories(pack, now);
            await dbContext.SaveChangesAsync();
        }
        else if (existingPack.LogicalPackageId == Guid.Empty)
        {
            // Backfill LogicalPackageId if migrating from Etap 5B
            existingPack.LogicalPackageId = StarterLogicalPackageId;
            existingPack.Version = 1;
            existingPack.Status = ContentPackageStatus.Published;
            existingPack.PublishedAtUtc ??= now;
            if (string.IsNullOrEmpty(existingPack.ConcurrencyToken))
            {
                existingPack.ConcurrencyToken = Guid.NewGuid().ToString("N");
            }
            await dbContext.SaveChangesAsync();
        }
    }

    private static void AddCategories(GamePackage pack, DateTimeOffset now)
    {
        AddCategory(pack, "school", "Szkoła", "School", 0, new[]
        {
            ("school_grades", "Kto miał najlepsze oceny w szkole?", "Who had the best grades at school?"),
            ("school_late", "Kto najczęściej spóźniał się na lekcje?", "Who was late for classes the most?"),
            ("school_teacher", "Kto najlepiej poradziłby sobie jako nauczyciel?", "Who would make the best teacher?"),
            ("school_homework", "Kto zawsze odpisywał zadania domowe na przerwie?", "Who always copied homework during break?"),
            ("school_principal", "Kto najczęściej lądował na dywaniku u dyrektora?", "Who visited the principal's office the most?")
        }, new[]
        {
            ("school_txt_1", "Gdyby {player} został(a) dyrektorem szkoły, jaka byłaby pierwsza zasada?", "If {player} became the principal, what would be the first rule?"),
            ("school_txt_2", "Za co {player} najczęściej obrywał(a) w szkole?", "What did {player} get in trouble for the most at school?"),
            ("school_txt_3", "Czego {player} uczyłby(aby) jako nauczyciel?", "What would {player} teach as a teacher?"),
            ("school_txt_4", "Gdzie {player} najczęściej chował(a) się na wagarach?", "Where did {player} hide the most when skipping school?"),
            ("school_txt_5", "Co {player} napisałby(aby) w szkolnym roczniku?", "What would {player} write in the yearbook?")
        }, now);

        AddCategory(pack, "work", "Praca", "Work", 1, new[]
        {
            ("work_boss", "Kto byłby najlepszym szefem?", "Who would make the best boss?"),
            ("work_fired", "Kto pierwszy zostałby zwolniony?", "Who would get fired first?"),
            ("work_overtime", "Kto bierze najwięcej nadgodzin?", "Who takes the most overtime?"),
            ("work_coffee", "Kto wypija najwięcej kawy w biurze?", "Who drinks the most coffee in the office?"),
            ("work_gossip", "Kto plotkuje najwięcej w pracy?", "Who gossips the most at work?")
        }, new[]
        {
            ("work_txt_1", "Na co {player} wydał(a)by pierwszą wypłatę?", "What would {player} spend their first paycheck on?"),
            ("work_txt_2", "Jak {player} tłumaczyłby(aby) spóźnienie do pracy?", "How would {player} excuse being late to work?"),
            ("work_txt_3", "Co {player} robi, gdy szef nie patrzy?", "What does {player} do when the boss isn't looking?"),
            ("work_txt_4", "Jaką firmę założyłby(aby) {player}?", "What company would {player} start?"),
            ("work_txt_5", "O czym {player} najczęściej rozmawia na przerwie?", "What does {player} talk about the most during breaks?")
        }, now);

        AddCategory(pack, "vacation", "Wakacje", "Vacations", 2, new[]
        {
            ("vacation_lost", "Kto pierwszy zgubiłby się na wakacjach?", "Who would get lost first on vacation?"),
            ("vacation_packing", "Kto bierze najwięcej niepotrzebnych rzeczy na wyjazd?", "Who packs the most unnecessary things for a trip?"),
            ("vacation_sunburn", "Kto najszybciej spiekłby się na raka?", "Who would get sunburned the fastest?"),
            ("vacation_guide", "Kto najchętniej zostałby przewodnikiem wycieczki?", "Who would most likely become a tour guide?"),
            ("vacation_lazy", "Kto nie schodziłby z leżaka przez całe wakacje?", "Who wouldn't leave their sunbed for the whole vacation?")
        }, new[]
        {
            ("vacation_txt_1", "Co {player} na pewno zapomni spakować?", "What will {player} definitely forget to pack?"),
            ("vacation_txt_2", "Gdzie {player} pojechał(a)by na wymarzone wakacje?", "Where would {player} go for a dream vacation?"),
            ("vacation_txt_3", "Co {player} przywiózł(aby) jako pamiątkę?", "What would {player} bring back as a souvenir?"),
            ("vacation_txt_4", "Z jakiego powodu {player} mógłby(aby) zostać wyrzucony(a) z hotelu?", "Why could {player} get kicked out of a hotel?"),
            ("vacation_txt_5", "Jakie jedno słowo opisuje wakacje z {player}?", "What one word describes a vacation with {player}?")
        }, now);

        AddCategory(pack, "party", "Impreza", "Party", 3, new[]
        {
            ("party_dance", "Kto jest królem parkietu?", "Who is the king of the dance floor?"),
            ("party_sleep", "Kto pierwszy usnąłby na imprezie?", "Who would fall asleep first at a party?"),
            ("party_dj", "Kto powinien być DJ-em dzisiejszej imprezy?", "Who should be the DJ for tonight's party?"),
            ("party_loud", "Kto zawsze jest najgłośniejszy na domówce?", "Who is always the loudest at a house party?"),
            ("party_host", "Kto organizuje najlepsze imprezy?", "Who throws the best parties?")
        }, new[]
        {
            ("party_txt_1", "Co {player} śpiewa pod prysznicem przed imprezą?", "What does {player} sing in the shower before a party?"),
            ("party_txt_2", "Jaki jest popisowy taniec {player}?", "What is {player}'s signature dance move?"),
            ("party_txt_3", "Co {player} zamawia przy barze?", "What does {player} order at the bar?"),
            ("party_txt_4", "Jak {player} wymiguje się od płacenia za taksówkę?", "How does {player} get out of paying for the cab?"),
            ("party_txt_5", "Co {player} ukradłby(aby) z imprezy na pamiątkę?", "What would {player} steal from a party as a souvenir?")
        }, now);

        AddCategory(pack, "relationships", "Związki", "Relationships", 4, new[]
        {
            ("rel_romantic", "Kto jest największym romantykiem?", "Who is the biggest romantic?"),
            ("rel_argue", "Kto pierwszy kłóci się o byle co?", "Who argues first about trivial things?"),
            ("rel_forget", "Kto najszybciej zapomniałby o rocznicy?", "Who would forget an anniversary the fastest?"),
            ("rel_dating", "Kto miał najgorsze randki?", "Who had the worst dates?"),
            ("rel_marry", "Kto weźmie ślub jako pierwszy?", "Who will get married first?")
        }, new[]
        {
            ("rel_txt_1", "Jaki jest idealny pomysł na randkę wg {player}?", "What is the perfect date idea according to {player}?"),
            ("rel_txt_2", "Czego {player} najbardziej szuka w partnerze?", "What does {player} look for most in a partner?"),
            ("rel_txt_3", "Jak {player} zareagowałby(aby) na zdradę?", "How would {player} react to cheating?"),
            ("rel_txt_4", "Jak {player} podrywa w barze?", "How does {player} flirt at a bar?"),
            ("rel_txt_5", "Co {player} napisałby(aby) na swoim profilu randkowym?", "What would {player} write on their dating profile?")
        }, now);

        AddCategory(pack, "everyday", "Codzienność", "Everyday life", 5, new[]
        {
            ("day_late", "Kto najdłużej przygotowuje się do wyjścia?", "Who takes the longest to get ready?"),
            ("day_phone", "Kto najdłużej siedzi na telefonie?", "Who spends the most time on their phone?"),
            ("day_messy", "Kto ma największy bałagan w pokoju?", "Who has the messiest room?"),
            ("day_cooking", "Kto zawsze zamawia jedzenie zamiast ugotować?", "Who always orders food instead of cooking?"),
            ("day_sleep", "Kto najdłużej śpi w weekendy?", "Who sleeps the longest on weekends?")
        }, new[]
        {
            ("day_txt_1", "Bez czego {player} nie wyjdzie z domu?", "What won't {player} leave the house without?"),
            ("day_txt_2", "O czym {player} najczęściej myśli przed snem?", "What does {player} think about most before sleep?"),
            ("day_txt_3", "Co {player} robi w niedzielę rano?", "What does {player} do on a Sunday morning?"),
            ("day_txt_4", "Jaka jest największa codzienna udręka {player}?", "What is {player}'s biggest everyday struggle?"),
            ("day_txt_5", "Na co {player} marnuje najwięcej czasu?", "What does {player} waste the most time on?")
        }, now);

        AddCategory(pack, "food", "Jedzenie", "Food", 6, new[]
        {
            ("food_fast", "Kto zjadłby najwięcej fast foodów?", "Who would eat the most fast food?"),
            ("food_chef", "Kto najlepiej gotuje?", "Who is the best cook?"),
            ("food_spicy", "Kto zjadłby najostrzejszą papryczkę bez popijania?", "Who would eat the spiciest pepper without a drink?"),
            ("food_sweet", "Kto jest największym łasuchem?", "Who has the biggest sweet tooth?"),
            ("food_share", "Kto nigdy nie dzieli się jedzeniem?", "Who never shares their food?")
        }, new[]
        {
            ("food_txt_1", "Co {player} zjadłby(aby) jako ostatni posiłek?", "What would {player} eat as their last meal?"),
            ("food_txt_2", "Jakie jest ulubione comfort food {player}?", "What is {player}'s favorite comfort food?"),
            ("food_txt_3", "Co {player} wyjada po nocach z lodówki?", "What does {player} sneak from the fridge at night?"),
            ("food_txt_4", "Czego {player} absolutnie nie zje?", "What will {player} absolutely not eat?"),
            ("food_txt_5", "Z czego składa się idealna pizza wg {player}?", "What makes up the perfect pizza according to {player}?")
        }, now);

        AddCategory(pack, "tech", "Technologia", "Technology", 7, new[]
        {
            ("tech_hacker", "Kto najlepiej zna się na komputerach?", "Who knows the most about computers?"),
            ("tech_break", "Kto najczęściej psuje elektronikę?", "Who breaks their electronics the most?"),
            ("tech_latest", "Kto zawsze ma najnowsze gadżety?", "Who always has the latest gadgets?"),
            ("tech_help", "Kto zawsze prosi o pomoc z technologią?", "Who always asks for help with technology?"),
            ("tech_games", "Kto spędza najwięcej czasu na grach?", "Who spends the most time playing games?")
        }, new[]
        {
            ("tech_txt_1", "Co {player} ma na tapecie telefonu?", "What is on {player}'s phone wallpaper?"),
            ("tech_txt_2", "Jakie jest ulubione hasło {player}?", "What is {player}'s favorite password?"),
            ("tech_txt_3", "Bez jakiej aplikacji {player} nie może żyć?", "Which app can't {player} live without?"),
            ("tech_txt_4", "Dlaczego {player} ma zepsuty ekran w telefonie?", "Why does {player} have a cracked phone screen?"),
            ("tech_txt_5", "Jak {player} naprawia niedziałający internet?", "How does {player} fix broken internet?")
        }, now);

        AddCategory(pack, "travel", "Podróże", "Travel", 8, new[]
        {
            ("travel_world", "Kto najszybciej objechałby świat dookoła?", "Who would travel around the world the fastest?"),
            ("travel_spontaneous", "Kto jest najbardziej spontaniczny w podróżach?", "Who is the most spontaneous traveler?"),
            ("travel_map", "Kto najlepiej posługuje się mapą?", "Who is the best at reading maps?"),
            ("travel_complain", "Kto najwięcej narzeka w trakcie długiej jazdy?", "Who complains the most during a long drive?"),
            ("travel_culture", "Kto najchętniej próbowałby dziwnych lokalnych potraw?", "Who is the most willing to try weird local food?")
        }, new[]
        {
            ("travel_txt_1", "Jaki kraj {player} chciałby(aby) odwiedzić najbardziej?", "Which country does {player} want to visit most?"),
            ("travel_txt_2", "Z czym {player} zawsze wraca z podróży?", "What does {player} always return with from a trip?"),
            ("travel_txt_3", "Gdzie {player} najpewniej by się zgubił(a)?", "Where is {player} most likely to get lost?"),
            ("travel_txt_4", "Jaki jest wymarzony środek transportu {player}?", "What is {player}'s dream mode of transportation?"),
            ("travel_txt_5", "Za co {player} dostał(a)by mandat na wakacjach?", "What would {player} get a fine for on vacation?")
        }, now);

        AddCategory(pack, "character", "Charakter", "Character", 9, new[]
        {
            ("char_funny", "Kto ma najlepsze poczucie humoru?", "Who has the best sense of humor?"),
            ("char_stubborn", "Kto jest najbardziej uparty?", "Who is the most stubborn?"),
            ("char_secret", "Komu można powierzyć największy sekret?", "Who can be trusted with the biggest secret?"),
            ("char_panic", "Kto pierwszy wpada w panikę w stresowej sytuacji?", "Who panics first in a stressful situation?"),
            ("char_smile", "Kto najczęściej się uśmiecha?", "Who smiles the most?")
        }, new[]
        {
            ("char_txt_1", "Jaka jest najgorsza cecha charakteru {player}?", "What is {player}'s worst character trait?"),
            ("char_txt_2", "O co najłatwiej pokłócić się z {player}?", "What is the easiest thing to argue about with {player}?"),
            ("char_txt_3", "Co {player} robi, gdy nikt nie patrzy?", "What does {player} do when no one is watching?"),
            ("char_txt_4", "W jakiej sytuacji {player} zawsze kłamie?", "In what situation does {player} always lie?"),
            ("char_txt_5", "Jakim zwierzęciem byłby(aby) {player}?", "What animal would {player} be?")
        }, now);
    }

    private static void AddCategory(GamePackage pack, string key, string namePl, string nameEn, int order, (string qKey, string qPl, string qEn)[] psQuestions, (string qKey, string qPl, string qEn)[] txtQuestions, DateTimeOffset now)
    {
        var catKey = $"{pack.Key}_{key}";
        var category = pack.Categories.FirstOrDefault(c => c.Key == catKey);
        if (category is null)
        {
            category = new GameCategory
            {
                Id = Guid.NewGuid(),
                PackageId = pack.Id,
                Key = catKey,
                NamePl = namePl,
                NameEn = nameEn,
                DescriptionPl = "",
                DescriptionEn = "",
                IsActive = true,
                SortOrder = order,
                ConcurrencyToken = Guid.NewGuid().ToString("N"),
                Package = pack
            };
            pack.Categories.Add(category);
        }

        for (var i = 0; i < psQuestions.Length; i++)
        {
            var q = psQuestions[i];
            var qFullKey = $"{catKey}_{q.qKey}";
            if (!category.Questions.Any(x => x.Key == qFullKey))
            {
                category.Questions.Add(new GameQuestion
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Key = qFullKey,
                    Type = QuestionType.PlayerSelection,
                    TextPl = q.qPl,
                    TextEn = q.qEn,
                    IsActive = true,
                    MinimumPlayers = 3,
                    SortOrder = i,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    ConcurrencyToken = Guid.NewGuid().ToString("N"),
                    Category = category
                });
            }
        }

        for (var i = 0; i < txtQuestions.Length; i++)
        {
            var q = txtQuestions[i];
            var qFullKey = $"{catKey}_{q.qKey}";
            if (!category.Questions.Any(x => x.Key == qFullKey))
            {
                category.Questions.Add(new GameQuestion
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Key = qFullKey,
                    Type = QuestionType.TextAnswer,
                    TextPl = q.qPl,
                    TextEn = q.qEn,
                    IsActive = true,
                    MinimumPlayers = 3,
                    SortOrder = psQuestions.Length + i,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    ConcurrencyToken = Guid.NewGuid().ToString("N"),
                    Category = category
                });
            }
        }

        var photoPrompts = new (string Pl, string En)[]
        {
            ($"Zrób zdjęcie przedmiotu, który kojarzy się z kategorią „{namePl}” — bez fotografowania osób.", $"Photograph an object associated with “{nameEn}” — without photographing people."),
            ($"Zrób zdjęcie najzabawniejszego przedmiotu pasującego do kategorii „{namePl}”.", $"Photograph the funniest object matching “{nameEn}”."),
            ($"Zrób zdjęcie najbardziej zaskakującego przedmiotu w pobliżu związanego z kategorią „{namePl}”.", $"Photograph the most surprising nearby object related to “{nameEn}”."),
            ($"Zrób zdjęcie rzeczy, która mogłaby być rekwizytem w filmie o kategorii „{namePl}”.", $"Photograph something that could be a prop in a movie about “{nameEn}”."),
            ($"Zrób zdjęcie przedmiotu w ciekawym kolorze, który pasuje do kategorii „{namePl}”.", $"Photograph an interestingly colored object matching “{nameEn}”.")
        };
        for (var i = 0; i < photoPrompts.Length; i++)
        {
            var qFullKey = $"{catKey}_photo_{i + 1}";
            if (!category.Questions.Any(x => x.Key == qFullKey))
            {
                category.Questions.Add(new GameQuestion
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Key = qFullKey,
                    Type = QuestionType.PhotoAnswer,
                    TextPl = photoPrompts[i].Pl,
                    TextEn = photoPrompts[i].En,
                    IsActive = true,
                    MinimumPlayers = 3,
                    SortOrder = psQuestions.Length + txtQuestions.Length + i,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    ConcurrencyToken = Guid.NewGuid().ToString("N"),
                    Category = category
                });
            }
        }

        var drawingPrompts = new (string Pl, string En)[]
        {
            ($"Narysuj kota w stylu kategorii „{namePl}”.", $"Draw a cat in the style of “{nameEn}”."),
            ($"Narysuj najdziwniejszy przedmiot pasujący do kategorii „{namePl}”.", $"Draw the strangest object fitting “{nameEn}”."),
            ($"Narysuj pojazd przyszłości związany z kategorią „{namePl}”.", $"Draw a future vehicle related to “{nameEn}”."),
            ($"Narysuj potwora, który boi się kategorii „{namePl}”.", $"Draw a monster afraid of “{nameEn}”."),
            ($"Narysuj logo najdziwniejszej restauracji w kategorii „{namePl}”.", $"Draw a logo for the strangest restaurant in “{nameEn}”.")
        };
        for (var i = 0; i < drawingPrompts.Length; i++)
        {
            var qFullKey = $"{catKey}_drawing_{i + 1}";
            if (!category.Questions.Any(x => x.Key == qFullKey))
            {
                category.Questions.Add(new GameQuestion
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Key = qFullKey,
                    Type = QuestionType.DrawingAnswer,
                    TextPl = drawingPrompts[i].Pl,
                    TextEn = drawingPrompts[i].En,
                    IsActive = true,
                    MinimumPlayers = 3,
                    SortOrder = psQuestions.Length + txtQuestions.Length + photoPrompts.Length + i,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    ConcurrencyToken = Guid.NewGuid().ToString("N"),
                    Category = category
                });
            }
        }
    }
}
