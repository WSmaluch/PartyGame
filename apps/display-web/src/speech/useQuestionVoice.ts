import { useEffect, useState } from 'react';
import { gameQuestion, localizedText, type RoomSnapshot } from '../api/types';
import { QuestionVoiceController } from './questionVoice';

export function useQuestionVoice(snapshot: RoomSnapshot): void {
  const [controller] = useState(() => new QuestionVoiceController());
  const game = snapshot.game;
  const question = game ? gameQuestion(game) : undefined;
  const questionId = question?.instanceId ?? question?.id;
  const polish = navigator.language.toLowerCase().startsWith('pl');
  const text = localizedText(question?.text);

  useEffect(() => {
    controller.update({
      enabled: snapshot.settings.questionVoiceEnabled === true,
      roomCode: snapshot.roomCode,
      stage: game?.stage ?? 'NotStarted',
      questionId,
      text,
      language: polish ? 'pl-PL' : 'en-US',
    });
  }, [controller, game?.stage, polish, questionId, snapshot.roomCode, snapshot.settings.questionVoiceEnabled, text]);

  useEffect(() => () => controller.dispose(), [controller]);
}
