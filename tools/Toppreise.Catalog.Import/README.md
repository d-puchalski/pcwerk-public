# Toppreise Catalog Import

Jedyny importer katalogu dla MVP. Pobiera do 100 najpopularniejszych produktów z każdej aktywnej kategorii zapisanej w `main.source_categories`, a następnie otwiera strony wszystkich nowych i istniejących produktów, aby odświeżyć dane techniczne i wszystkie widoczne oferty sklepów.

Importer zapisuje dane relacyjne: aktualny ranking, historię rankingów i ofert, typowane tabele specyfikacji oraz błędy przebiegu. Brak produktu w rankingu nie wycofuje go z katalogu. Produkt otrzymuje status `retired` dopiero po poprawnym odczytaniu jego strony bez żadnej dostępnej oferty. Na końcu przebiegu importer automatycznie przebudowuje presety w `main.pc_preset_products` za pomocą generatora C#.

1. Utwórz bazę skryptem `db/create.sql`.
2. Ustaw `ConnectionStrings:PcWerk`.
3. Zainstaluj przeglądarkę Playwright, jeżeli nie jest dostępna.
4. W sekcji `ToppreiseImport:Categories` w `appsettings.json` ustaw `true` dla kategorii, które mają być importowane, i `false` dla pomijanych. Przełącznik `COOLING` steruje obiema kategoriami źródłowymi chłodzenia (powietrznym i AIO). Wyłączenie kategorii pomija jej odświeżanie, ale nie usuwa wcześniej zaimportowanych produktów z bazy.
5. Po pełnym imporcie generator C# tworzy kombinacje z profili sekcji `PcPresets` w `appsettings.json`. Profil `Integrated` wymaga CPU z iGPU i płyty z rozpoznanym wyjściem obrazu, ale nie dodaje osobnej karty graficznej. Profil `Discrete` wymaga co najmniej jednego modelu GPU. Publikowane są tylko kombinacje, dla których da się dobrać wszystkie dostępne i kompatybilne komponenty. Błąd przebudowy presetów jest zapisywany w błędach przebiegu, ale nie cofa poprawnie odświeżonych produktów.

Istniejącą bazę utworzoną przed obsługą iGPU zaktualizuj skryptem `db/upgrade_integrated_graphics.sql`. Obsługę laptopów dodaje `db/upgrade_laptops.sql`. Następnie wykonaj pełny import, aby parser w wersji 1.4.0 odświeżył dane. Cena sprzedaży laptopa jest zapisywana podczas importu jako cena najlepszej dostępnej oferty Toppreise powiększona o większą z wartości: 10% albo CHF 150.

Domyślnie importer uruchamia widoczny Chrome przez Playwright (`Headless=false`) i zachowuje profil między uruchomieniami. Jeżeli Toppreise zażąda ręcznej weryfikacji dostępu, wykonaj ją w otwartym oknie Chrome i potwierdź klawiszem ENTER w konsoli importera.

Dla każdej strony produktu importer zapisuje `OFERTY OK` z liczbą poprawnie odczytanych ofert. Jeżeli wiersz oferty nie zawiera identyfikatora, sklepu lub ceny całkowitej, log zawiera liczbę wierszy odczytanych, poprawnych i pominiętych. Następny komunikat `DODANO`, `ZAKTUALIZOWANO`, `BEZ ZMIAN` albo `UKRYTO` opisuje wynik zapisu produktu do katalogu.

## Przebudowa samych presetów

Aby przebudować presety z produktów i ofert już zapisanych w bazie, bez uruchamiania przeglądarki i importu Toppreise:

```powershell
dotnet run --project tools/Toppreise.Catalog.Import -- --presets-only
```
