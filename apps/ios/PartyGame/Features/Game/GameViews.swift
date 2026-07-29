import SwiftUI

struct CategoryIntroView: View {
    let category: GameCategorySnapshot?
    var body: some View {
        VStack {
            Text("category.intro.title")
                .font(.headline)
            Text(category?.name ?? "Unknown Category")
                .font(.largeTitle.bold())
                // .foregroundColor(Color(hex: category?.backgroundHexColor ?? "#FFFFFF"))
        }
    }
}

struct QuestionIntroView: View {
    let question: GameQuestionSnapshot?
    var body: some View {
        VStack {
            Text("question.intro.title")
                .font(.headline)
            Text(question?.questionText.local ?? "Unknown Question")
                .font(.title)
                .multilineTextAlignment(.center)
                .padding()
        }
    }
}

struct CollectingPlayerSelectionsView: View {
    let store: GameSessionStore
    let question: GameQuestionSnapshot?
    let players: [RoomPlayer]
    @State private var selectedId: UUID? = nil

    var body: some View {
        VStack {
            Text(question?.questionText.local ?? "")
                .font(.title2)
                .padding()
            Text("selection.prompt")
            
            let otherPlayers = players.filter { $0.id != store.ownPlayer?.id }
            ScrollView(.horizontal) {
                HStack {
                    ForEach(otherPlayers) { player in
                        Button(action: {
                            selectedId = player.id
                            Task {
                                await store.submitPlayerSelection(selectedPlayerId: player.id)
                            }
                        }) {
                            VStack {
                                AsyncImage(url: store.profilePhotoURL(for: player)) { image in
                                    image.resizable().scaledToFill()
                                } placeholder: {
                                    Color.gray
                                }
                                .frame(width: 80, height: 80)
                                .clipShape(Circle())
                                .overlay(
                                    Circle().stroke(selectedId == player.id ? Color.green : Color.clear, lineWidth: 4)
                                )
                                Text(player.nickname)
                            }
                        }
                        .disabled(store.submittedQuestionInstanceIds.contains(question?.instanceId ?? UUID()) || store.isWorking)
                    }
                }
                .padding()
            }
        }
    }
}

struct ShowingQuestionResultsView: View {
    let results: PlayerSelectionResults?
    let players: [RoomPlayer]
    let store: GameSessionStore
    
    @State private var revealedIndex = -1
    
    var body: some View {
        VStack {
            Text("results.title")
                .font(.largeTitle)
            
            if let results = results {
                ScrollView {
                    ForEach(Array(results.results.enumerated()), id: \.element.playerId) { index, voter in
                        if index <= revealedIndex {
                            HStack {
                                if let p = players.first(where: { $0.id == voter.playerId }) {
                                    Text(p.nickname)
                                }
                                Text(" -> ")
                                if let sp = players.first(where: { $0.id == voter.selectedPlayerId }) {
                                    Text(sp.nickname)
                                }
                                Spacer()
                                Text("+\(voter.pointsAwarded) pts")
                                    .bold()
                            }
                            .padding()
                            .transition(.slide)
                        }
                    }
                }
                .onAppear {
                    revealedIndex = -1
                    animateResults(count: results.results.count)
                }
            } else {
                Text("results.no_data")
            }
        }
    }
    
    private func animateResults(count: Int) {
        for i in 0..<count {
            DispatchQueue.main.asyncAfter(deadline: .now() + Double(i) * 1.5) {
                withAnimation {
                    revealedIndex = i
                }
            }
        }
    }
}

struct RoundSummaryView: View {
    let summary: RoundSummarySnapshot?
    let players: [RoomPlayer]
    
    var body: some View {
        VStack {
            Text("round.summary.title")
                .font(.largeTitle)
            
            if let summary = summary {
                List(summary.rankings.sorted(by: { $0.position < $1.position }), id: \.playerId) { ranking in
                    HStack {
                        Text("#\(ranking.position)")
                            .font(.headline)
                        if let p = players.first(where: { $0.id == ranking.playerId }) {
                            Text(p.nickname)
                        }
                        Spacer()
                        Text("\(ranking.score) pts")
                    }
                }
            } else {
                Text("round.summary.no_data")
            }
        }
    }
}

struct PausedForDisplayView: View {
    var body: some View {
        VStack {
            Image(systemName: "pause.circle.fill")
                .font(.system(size: 64))
            Text("game.paused_for_display")
                .font(.title)
        }
    }
}

struct CompletedView: View {
    let summary: RoundSummarySnapshot?
    let players: [RoomPlayer]
    var body: some View {
        VStack {
            Text("game.completed")
                .font(.largeTitle)
            
            if let summary = summary {
                List(summary.rankings.sorted(by: { $0.position < $1.position }), id: \.playerId) { ranking in
                    HStack {
                        Text("#\(ranking.position)")
                            .font(.headline)
                        if let p = players.first(where: { $0.id == ranking.playerId }) {
                            Text(p.nickname)
                        }
                        Spacer()
                        Text("\(ranking.score) pts")
                    }
                }
            }
        }
        .accessibilityIdentifier("game-completed-view")
    }
}
struct CollectingTextAnswersView: View {
    let store: GameSessionStore
    let question: GameQuestionSnapshot?
    
    @State private var answerText: String = ""
    
    var isSubmitted: Bool {
        store.privateGameState?.hasSubmittedTextAnswer == true
    }
    
    var isValidLength: Bool {
        let count = answerText.count
        return count > 0 && count <= 150
    }
    
    var body: some View {
        VStack {
            Text(question?.questionText.local ?? "")
                .font(.title2)
                .padding()
            
            if isSubmitted {
                Text("textanswer.waiting")
                    .font(.headline)
                    .foregroundColor(.secondary)
                    .accessibilityIdentifier("textanswer.waiting")
            } else {
                TextEditor(text: $answerText)
                    .padding()
                    .border(Color.gray)
                    .frame(height: 120)
                    .accessibilityIdentifier("textanswer.input")
                
                Text("\(answerText.count)/150")
                    .foregroundColor(answerText.count > 150 ? .red : .secondary)
                
                Button("action.submit") {
                    let submittedText = answerText
                    Task {
                        await store.submitTextAnswer(text: submittedText)
                    }
                }
                .disabled(!isValidLength || store.isWorking)
                .buttonStyle(.borderedProminent)
                .padding()
                .accessibilityIdentifier("textanswer.submit")
            }
        }
    }
}

struct RevealingTextAnswersView: View {
    var body: some View {
        VStack {
            Image(systemName: "eye")
                .font(.system(size: 64))
            Text("textanswer.revealing")
                .font(.title)
        }
    }
}

struct CollectingTextAnswerVotesView: View {
    let store: GameSessionStore
    let question: GameQuestionSnapshot?
    let results: TextAnswerResults?
    
    var isSubmitted: Bool {
        store.privateGameState?.hasSubmittedTextAnswerVote == true
    }

    var isEligible: Bool {
        store.privateGameState?.isEligibleForTextAnswerVote == true
    }
    
    var body: some View {
        VStack {
            Text(question?.questionText.local ?? "")
                .font(.title2)
                .padding()
            
            if isSubmitted || !isEligible {
                Text("textanswer.vote_waiting")
                    .font(.headline)
                    .foregroundColor(.secondary)
                    .accessibilityIdentifier("textanswer.vote_waiting")
            } else {
                Text("textanswer.vote_prompt")
                
                ScrollView {
                    VStack(spacing: 12) {
                        if let options = results?.votingOptions {
                            ForEach(options) { option in
                                let isOwn = store.privateGameState?.ownTextAnswerId == option.answerId
                                
                                Button(action: {
                                    Task {
                                        await store.submitTextAnswerVote(selectedAnswerId: option.answerId)
                                    }
                                }) {
                                    HStack {
                                        Text(option.text)
                                            .multilineTextAlignment(.leading)
                                        Spacer()
                                        if isOwn {
                                            Text("textanswer.own_answer_marker")
                                                .font(.caption)
                                                .foregroundColor(.secondary)
                                        }
                                    }
                                    .padding()
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                }
                                .disabled(store.isWorking || isOwn)
                                .buttonStyle(.bordered)
                                .accessibilityIdentifier("textanswer.vote_option")
                            }
                        }
                    }
                    .padding()
                }
            }
        }
    }
}

struct ShowingTextAnswerResultsView: View {
    let results: TextAnswerResults?
    let store: GameSessionStore
    
    var body: some View {
        VStack {
            Text("textanswer.results_title")
                .font(.largeTitle)
            
            if let options = results?.options {
                ScrollView {
                    VStack(spacing: 16) {
                        ForEach(options) { option in
                            VStack(alignment: .leading, spacing: 4) {
                                HStack {
                                    Text(option.text)
                                        .font(.headline)
                                    Spacer()
                                    Text("\(option.voteCount) votes")
                                        .bold()
                                        .foregroundColor(option.isTopResult ? .green : .primary)
                                }
                                
                                HStack {
                                    Text("By: \(option.authorPlayerNickname)")
                                        .font(.subheadline)
                                        .foregroundColor(.secondary)
                                    Spacer()
                                }
                                
                                if !option.voters.isEmpty {
                                    Text("Voted by: \(option.voters.map { voter in store.snapshot?.players.first(where: { $0.id == voter.playerId })?.nickname ?? "Unknown" }.joined(separator: ", "))")
                                        .font(.caption)
                                        .foregroundColor(.secondary)
                                }
                                
                                // points awarded if we have it
                                // Wait, the prompt says "pointsAwarded". But pointsAwarded is inside voters for TextAnswer? No, TextAnswer results has voters which is `[ResultVoter]`.
                                if option.voters.contains(where: { $0.pointsAwarded > 0 }) {
                                    Text("Points awarded!") // We can refine this
                                        .font(.caption2)
                                }
                            }
                            .padding()
                            .background(Color.secondary.opacity(0.1))
                            .cornerRadius(8)
                        }
                    }
                    .padding()
                }
            } else {
                Text("textanswer.no_results")
            }
        }
    }
}
