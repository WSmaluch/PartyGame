# Etap 6A — Admin Content Editor

Zakres Etapu 6A obejmuje zarządzanie kategoriami, listę i formularze czterech typów pytań oraz publikację, wersjonowanie, archiwizację i trwałe przypisanie wersji treści do pokoju. Kategorie obsługują listowanie, tworzenie, edycję, włączanie i wyłączanie, jawne tryby usuwania, przenoszenie pytań, atomowy reorder oraz optymistyczną współbieżność opartą na jawnych tokenach GUID.

Panel ukrywa mutacje dla wersji `Published` i `Archived`. Po konflikcie 409 zachowuje formularz i oferuje odświeżenie danych albo pozostanie przy wpisanych zmianach. Tekst jest renderowany przez React jako plain text; kod nie używa `dangerouslySetInnerHTML`.

Modal usuwania kategorii otrzymuje fokus po otwarciu, zamyka się klawiszem Escape i przy anulowaniu oddaje fokus przyciskowi, który go otworzył. Błędy zapisu są powiązane z polami formularza przez `aria-describedby`.

## Lista pytań (6A.2A)

Lista pytań używa prawdziwego API, filtrów synchronizowanych z URL, paginacji i stabilnego sortowania. Obsługuje przełączanie aktywności, duplikowanie, usuwanie oraz reorder pełnej pojedynczej kategorii. Wszystkie mutacje używają tokenów współbieżności i po HTTP 409 wymagają ręcznego odświeżenia listy. Wersje `Published` i `Archived` zachowują listę oraz filtry, ale nie pokazują akcji modyfikujących. Pełne formularze typów pytań pozostają zakresem 6A.2B.

## Formularze i podgląd pytań (6A.2B)

Formularz tworzy i edytuje wszystkie cztery typy pytań, korzystając z endpointu szczegółów zamiast przeszukiwania listy. Zapewnia walidację UX zgodną z backendem, liczniki znaków, preview PL/EN i pomoc zależną od typu. Treść preview jest plain textem. Konflikt 409 zachowuje dane użytkownika i pozwala ręcznie odświeżyć szczegóły. Niezapisane zmiany są chronione przy powrocie do listy i przez `beforeunload`; powrót zachowuje query string filtrów. Dla `Published` i `Archived` formularz jest widokiem tylko do odczytu.

## Publikacja i wersjonowanie (6A.3)

Pakiet przechodzi przez `Draft`, `Published` i `Archived`. Draft pozwala edytować metadane i publikować po walidacji domenowej. Published można zarchiwizować lub skopiować do kolejnego Draftu; Archived można wyłącznie otworzyć albo skopiować do Draftu. Pakiety są wersjami jednej rodziny (`logicalPackageId`) i pokoje przypinają konkretną opublikowaną wersję.

Znane ograniczenie: Admin API nie ma jeszcze produkcyjnego uwierzytelniania i jest przeznaczone wyłącznie do zaufanego środowiska lokalnego.

## Concurrency guarantees and race behavior

Testy integracyjne uruchamiają prawdziwe równoległe żądania `Task.WhenAll` dla update/update, delete/update, reorder/update, moveQuestions/update pytania i deleteQuestions/update pytania. Każdy wariant dopuszcza tylko spójny zwycięski zapis; przegrana operacja jest konfliktem 409 albo kontrolowanym brakiem rekordu po delete. Wyjątki EF współbieżności są zamieniane na `content_concurrency_conflict`, a operacje move, delete i reorder zapisują się atomowo.

## Deferred E2E hardening

Status: **Implemented partially — execution deferred**.

`MixedGameClientE2ETests` posiada prawdziwe interakcje iOS obejmujące Join, systemowy `PhotosPicker`, zapis profilu, Ready oraz pierwszą odpowiedź DrawingAnswer. Pełne uruchomienie mixed-client wymaga jeszcze deterministycznego setupu Published package, pokoju oraz skryptowanych klientów SignalR w orkiestratorze.

Jest to dług infrastruktury E2E, a nie brak funkcjonalności produkcyjnej Etapu 6A. Zakres został przeniesiony do późniejszego hardeningu i nie blokuje odbioru Admin Content Editora.
# Etap 6A — Admin Content Editor

## 6A.3: publikacja, wersje i historyczne pokoje

Każda wersja ma własne `GamePackage.Id`; rodzina używa `LogicalPackageId`, a wersje mają rosnące `Version`. Draft tworzy się jako deep copy Published albo Archived. Kopia zachowuje metadane, klucze, aktywność, kolejność i treści, ale otrzymuje nowe ID i concurrency tokeny. Aktywny może być tylko jeden Draft w rodzinie; regułę egzekwują lock oraz indeks częściowy SQLite.

Panel Admina rozróżnia Draft, Published i Archived: tylko Draft ma formularze, Published daje publikację/archiwizację lub utworzenie Draftu, Archived tylko utworzenie Draftu. Dialog publikacji prezentuje liczniki i błędy walidacji. Błąd 409 nie czyści formularza; użytkownik może jawnie użyć „Odśwież dane”.

Pokój otrzymuje stałe `contentPackageVersionId`. Request bez tego pola zachowuje dawny wybór domyślnego Published Startera. Draft, Archived i nieistniejące ID są odrzucane dla nowego pokoju. Archiwizacja nie unieważnia istniejących pokojów, a publikacja następnej wersji nie zmienia ich powiązania.
