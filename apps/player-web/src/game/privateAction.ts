export type PrivateActionType =
  | 'TextAnswer'
  | 'TextAnswerVote'
  | 'PhotoAnswer'
  | 'PhotoAnswerVote'
  | 'DrawingAnswer'
  | 'DrawingAnswerVote';

export function privateActionType(
  stage: string,
): PrivateActionType | undefined {
  switch (stage) {
    case 'CollectingTextAnswers':
      return 'TextAnswer';
    case 'CollectingTextAnswerVotes':
      return 'TextAnswerVote';
    case 'CollectingPhotoAnswers':
      return 'PhotoAnswer';
    case 'CollectingPhotoAnswerVotes':
      return 'PhotoAnswerVote';
    case 'CollectingDrawingAnswers':
      return 'DrawingAnswer';
    case 'CollectingDrawingAnswerVotes':
      return 'DrawingAnswerVote';
    default:
      return undefined;
  }
}
