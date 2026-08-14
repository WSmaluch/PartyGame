import SwiftUI

struct FinalRoundEditView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    @State private var active: [DrawingPoint] = []

    var body: some View {
        VStack(spacing: 16) {
            Text("finalRound.editTitle").font(.title.bold())
            Text(String(format: String(localized: "finalRound.editProgress"), game.finalRound?.currentPass ?? 0, game.finalRound?.totalPasses ?? 0))
            if store.privateGameState?.finalRound?.hasSubmittedEdit == true {
                FinalRoundWaitingView(game: game, message: "finalRound.editSaved")
            } else if store.finalEditDraft == nil {
                Button("finalRound.startEdit") { store.openFinalEditCanvas() }
                    .buttonStyle(.borderedProminent).accessibilityIdentifier("final-round-edit-start")
            } else if let draft = store.finalEditDraft {
                editor(draft)
            } else {
                ProgressView()
            }
        }
        .padding()
            .accessibilityElement(children: .contain)
            .accessibilityIdentifier(store.privateGameState?.finalRound?.hasSubmittedEdit == true ? "final-round-edit-submitted-state" : "final-round-edit-ready-state")
    }

    private func editor(_ draft: FinalRoundEditDraft) -> some View {
        VStack(spacing: 12) {
            GeometryReader { proxy in
                ZStack {
                    AsyncImage(url: store.mediaURL(draft.sourceDisplayMediaUrl)) { image in
                        image.resizable().scaledToFill()
                    } placeholder: { ProgressView() }
                    Canvas { context, size in
                        for stroke in draft.canvas.completedStrokes { draw(stroke.points, color: stroke.tool == .eraser ? .white : stroke.color.color, width: stroke.lineWidth, context: context, size: size) }
                        draw(active, color: draft.canvas.selectedTool == .eraser ? .white : draft.canvas.selectedColor.color, width: draft.canvas.selectedLineWidth.rawValue, context: context, size: size)
                    }
                    Color.clear.contentShape(Rectangle()).gesture(DragGesture(minimumDistance: 0).onChanged { value in
                        guard proxy.size.width > 0, proxy.size.height > 0 else { return }
                        active.append(DrawingPoint(x: min(1, max(0, value.location.x / proxy.size.width)), y: min(1, max(0, value.location.y / proxy.size.height))))
                    }.onEnded { _ in
                        if active.isEmpty { active = [DrawingPoint(x: 0.5, y: 0.5)] }
                        var canvas = draft.canvas; canvas.complete(active); store.updateFinalEditCanvas(canvas); active = []
                    })
                }.clipShape(RoundedRectangle(cornerRadius: 16)).overlay(RoundedRectangle(cornerRadius: 16).stroke(.secondary)).accessibilityIdentifier("final-round-edit-canvas")
            }.frame(maxWidth: 420).frame(height: min(UIScreen.main.bounds.width - 56, 420))
            HStack { ForEach(DrawingColor.allCases, id: \.self) { color in
                Button { var canvas = draft.canvas; canvas.selectedColor = color; canvas.selectedTool = .brush; store.updateFinalEditCanvas(canvas) } label: { Circle().fill(color.color).frame(width: 30, height: 30) }
            } }
            HStack {
                ForEach(DrawingLineWidth.allCases, id: \.self) { width in Button { var canvas = draft.canvas; canvas.selectedLineWidth = width; store.updateFinalEditCanvas(canvas) } label: { Circle().fill(.primary).frame(width: max(18, width.rawValue), height: max(18, width.rawValue)) } }
                Button("drawing.eraser", systemImage: "eraser") { var canvas = draft.canvas; canvas.selectedTool = .eraser; store.updateFinalEditCanvas(canvas) }
            }
            HStack {
                Button("drawing.undo", systemImage: "arrow.uturn.backward") { var canvas = draft.canvas; canvas.undo(); store.updateFinalEditCanvas(canvas) }.disabled(draft.canvas.completedStrokes.isEmpty)
                Button("drawing.redo", systemImage: "arrow.uturn.forward") { var canvas = draft.canvas; canvas.redo(); store.updateFinalEditCanvas(canvas) }.disabled(draft.canvas.redoStack.isEmpty)
                Button("drawing.clear", role: .destructive) { var canvas = draft.canvas; canvas.clear(); store.updateFinalEditCanvas(canvas) }
            }
            Button("drawing.done") { Task { await store.previewFinalEdit() } }
                .buttonStyle(.borderedProminent)
                .disabled(draft.canvas.isEmpty)
                .accessibilityIdentifier("final-round-edit-preview")
            if let png = draft.previewPNG, let image = UIImage(data: png) {
                Image(uiImage: image).resizable().scaledToFit().frame(maxHeight: 220)
                switch store.drawingUploadPhase {
                case .uploading(let progress): ProgressView(value: progress)
                case .serverProcessing: ProgressView("drawing.saving")
                case .failed: Button("common.retry") { store.uploadFinalEdit() }
                default: Button("finalRound.sendEdit") { store.uploadFinalEdit() }.buttonStyle(.borderedProminent).accessibilityIdentifier("final-round-edit-send")
                }
            }
        }
    }

    private func draw(_ points: [DrawingPoint], color: Color, width: CGFloat, context: GraphicsContext, size: CGSize) {
        guard let first = points.first else { return }; var path = Path(); path.move(to: CGPoint(x: first.x * size.width, y: first.y * size.height)); for point in points.dropFirst() { path.addLine(to: CGPoint(x: point.x * size.width, y: point.y * size.height)) }; context.stroke(path, with: .color(color), style: StrokeStyle(lineWidth: width, lineCap: .round, lineJoin: .round))
    }
}

struct FinalRoundVotingView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    var body: some View {
        VStack(spacing: 14) {
            Text("finalRound.voteTitle").font(.title.bold())
            Text(String(format: String(localized: "finalRound.voteProgress"), game.finalRound?.submittedVotes ?? 0, game.finalRound?.requiredVotes ?? 0))
            ScrollView { LazyVGrid(columns: [GridItem(.adaptive(minimum: 145))]) {
                ForEach(game.finalRound?.artifacts ?? []) { artifact in
                    FinalRoundVoteCard(store: store, artifact: artifact)
                }
            } }
            Button("finalRound.vote") { Task { await store.submitSelectedFinalRoundVote() } }.buttonStyle(.borderedProminent).disabled(store.selectedFinalRoundVoteId == nil || store.isWorking).accessibilityIdentifier("final-round-vote-send")
        }.padding().accessibilityElement(children: .contain).accessibilityIdentifier("final-round-voting-view")
    }
}

private struct FinalRoundVoteCard: View {
    let store: GameSessionStore
    let artifact: FinalRoundArtifact
    private var selected: Bool { store.selectedFinalRoundVoteId == artifact.artifactId }
    var body: some View {
        Button { store.selectFinalRoundVote(artifact.artifactId) } label: {
            VStack {
                AsyncImage(url: store.mediaURL(artifact.thumbnailMediaUrl)) { image in image.resizable().scaledToFit() } placeholder: { ProgressView() }
                Text(artifact.targetRole.local)
                if selected { Image(systemName: "checkmark.circle.fill") }
            }
        }
        .buttonStyle(.plain).padding(8)
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(selected ? Color.accentColor : Color.secondary, lineWidth: selected ? 3 : 1))
        .accessibilityIdentifier("final-round-vote-\(artifact.artifactId.uuidString)")
    }
}

struct FinalRoundWaitingView: View {
    let game: GameSnapshot
    let message: LocalizedStringKey
    var body: some View { VStack(spacing: 16) { Image(systemName: "checkmark.circle.fill").font(.system(size: 60)).foregroundStyle(.green); Text(message).font(.title3.bold()); Text("finalRound.waiting") }.accessibilityElement(children: .contain).accessibilityIdentifier("final-round-waiting-view") }
}

struct FinalRoundPresentationView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    var body: some View { VStack(spacing: 14) { Text("finalRound.presentationTitle").font(.title.bold()); ScrollView { ForEach(game.finalRound?.artifacts ?? []) { artifact in VStack { AsyncImage(url: store.mediaURL(artifact.displayMediaUrl)) { $0.resizable().scaledToFit() } placeholder: { ProgressView() }; Text("\(artifact.subjectNickname) · \(artifact.targetRole.local)").bold() }.padding() } } }.accessibilityElement(children: .contain).accessibilityIdentifier("final-round-presentation-view") }
}

struct FinalRoundResultsView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    var body: some View { VStack(spacing: 14) { Text("finalRound.resultsTitle").font(.title.bold()); ScrollView { ForEach(game.finalRound?.artifacts ?? []) { artifact in VStack { AsyncImage(url: store.mediaURL(artifact.displayMediaUrl)) { $0.resizable().scaledToFit() } placeholder: { ProgressView() }; Text("\(artifact.subjectNickname) · \(artifact.targetRole.local)").bold(); Text(String(format: String(localized: "finalRound.votes"), artifact.voteCount)); if artifact.isTopResult { Image(systemName: "trophy.fill") } }.padding() } } }.accessibilityElement(children: .contain).accessibilityIdentifier("final-round-results-view") }
}
