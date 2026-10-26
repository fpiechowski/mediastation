// Czysta logika nagłówka Home (DESIGN.md → typography.greeting / clock; EXPERIENCE.md → Avatar): bez DOM i Reacta.

/** Imię w powitaniu: w MVP brak kont, więc stała. */
export const USER_NAME = 'Eliza';

/** Pierwsza godzina „Dzień dobry” (włącznie) i pierwsza godzina „Dobry wieczór” (włącznie). */
const MORNING_FROM_HOUR = 5;
const EVENING_FROM_HOUR = 18;

/** Powitanie wg pory dnia lokalnej: 05:00–17:59 „Dzień dobry”, reszta „Dobry wieczór”. */
export function greeting(date: Date): string {
  const hour = date.getHours();
  const salutation = hour >= MORNING_FROM_HOUR && hour < EVENING_FROM_HOUR ? 'Dzień dobry' : 'Dobry wieczór';
  return `${salutation}, ${USER_NAME}`;
}

const twoDigits = (n: number): string => String(n).padStart(2, '0');

/** Zegar 24h z dwiema cyframi godzin i minut („09:05”). */
export function formatClock(date: Date): string {
  return `${twoDigits(date.getHours())}:${twoDigits(date.getMinutes())}`;
}

/** Milisekundy do początku następnej minuty (zawsze 1–60000), żeby zegar tykał równo ze zmianą minuty. */
export function msToNextMinute(date: Date): number {
  return 60_000 - (date.getSeconds() * 1000 + date.getMilliseconds());
}

/** Inicjał avatara z imienia. */
export function initial(name: string = USER_NAME): string {
  return name.charAt(0).toUpperCase();
}
