import type { QuestionType } from '../../api/adminContentApi';

type Props = { type: QuestionType; textPl: string; textEn: string };
export function QuestionPreview({ type, textPl, textEn }: Props) {
  const content = (text: string, language: string) => <article><h4>{language}</h4>{text ? <p>{text}</p> : <p>Podgląd pojawi się po wpisaniu treści pytania.</p>}</article>;
  const typePreview = type === 'PlayerSelection' ? <p>Odpowiedzią jest wybór gracza.</p> : type === 'TextAnswer' ? <><p>Gracze wpisują własną odpowiedź tekstową.</p><input aria-label="Przykładowa odpowiedź tekstowa" disabled placeholder="Twoja odpowiedź" /><button disabled>Wyślij</button><p>Odpowiedzi są ujawniane anonimowo.</p></> : type === 'PhotoAnswer' ? <><p>Gracze wykonują lub wybierają zdjęcie jako odpowiedź.</p><div role="img" aria-label="Placeholder zdjęcia">□ Zdjęcie</div><button disabled>Dodaj zdjęcie</button></> : <><p>Gracze rysują odpowiedź na prostym płótnie dotykowym.</p><div aria-label="Placeholder płótna do rysowania" role="img">□ Płótno rysunku</div><button disabled>Ołówek</button></>;
  return <section aria-label="Podgląd pytania"><h3>Podgląd pytania</h3><div>{content(textPl, 'PL')}{content(textEn, 'EN')}</div>{typePreview}</section>;
}
