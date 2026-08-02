# Troubleshooting operatora

Najpierw uruchom `diagnose-lan.sh`, potem sprawdź `health/ready` i operator summary. `migration-required-or-incompatible` wymaga standardowej procedury migracji; `media unavailable` wymaga sprawdzenia runtime media root; `data operation active` oznacza, że lifecycle lock blokuje start.

Przy błędzie zapisz correlation ID z odpowiedzi. W logach wyszukuj po nim, nie po tokenie. Utwórz support bundle w trybie minimal, jeśli dysk jest ograniczony, albo standard/extended dla zgłoszenia. Zawsze uruchom verifier przed przekazaniem archiwum.

iOS: w ustawieniach serwera jest bezpieczne podsumowanie do skopiowania. Display i Admin pokazują wersję, stan połączenia i ostatnie bezpieczne correlation ID; żaden z widoków nie pokazuje tokenu.
