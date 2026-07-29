import SwiftUI

struct DrawingAnswerTaskView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    var body: some View {
        VStack(spacing: 18) {
            DrawingTaskHeader(game: game)
            Text(String(format: String(localized: "drawing.submissionProgress"), game.drawingAnswerResults?.submittedDrawingAnswers ?? 0, game.drawingAnswerResults?.requiredDrawingAnswers ?? 0))
            Button(store.drawingDraft == nil ? "drawing.start" : "drawing.continue") { store.openDrawingCanvas() }
                .buttonStyle(.borderedProminent).controlSize(.large).accessibilityIdentifier("drawing.start")
            if store.drawingDraft != nil { DrawingCanvasEditor(store: store) }
            if let error = store.errorMessage { Text(error).foregroundStyle(.red) }
        }.padding()
    }
}

struct DrawingCanvasEditor: View {
    let store: GameSessionStore
    @State private var active: [DrawingPoint] = []
    @State private var showClear = false

    var body: some View {
        guard let draft = store.drawingDraft else { return AnyView(EmptyView()) }
        return AnyView(VStack(spacing: 12) {
            GeometryReader { proxy in
                ZStack {
                    Canvas { context, size in
                        context.fill(Path(CGRect(origin: .zero, size: size)), with: .color(.white))
                        render(draft.canvas.completedStrokes, context: context, size: size)
                        renderActive(context: context, size: size, canvas: draft.canvas)
                    }
                    Color.clear
                        .contentShape(Rectangle())
                        .accessibilityElement()
                        .accessibilityLabel("drawing.canvas")
                        .accessibilityIdentifier("drawing-canvas")
                        .gesture(DragGesture(minimumDistance: 0).onChanged { value in
                            guard proxy.size.width > 0, proxy.size.height > 0 else { return }
                            active.append(DrawingPoint(x: min(1, max(0, value.location.x / proxy.size.width)), y: min(1, max(0, value.location.y / proxy.size.height))))
                        }.onEnded { _ in
                            if active.isEmpty { active = [DrawingPoint(x: 0.5, y: 0.5)] }
                            var state = draft.canvas; state.complete(active); store.updateDrawingCanvas(state); active = []
                        })
                }
                .clipShape(RoundedRectangle(cornerRadius: 16))
                .overlay(RoundedRectangle(cornerRadius: 16).stroke(.secondary))
            }
            .frame(maxWidth: 420)
            .frame(height: min(UIScreen.main.bounds.width - 56, 420))
            HStack { ForEach(DrawingColor.allCases, id: \.self) { color in
                Button { var state = draft.canvas; state.selectedColor = color; state.selectedTool = .brush; store.updateDrawingCanvas(state) } label: {
                    Circle().fill(color.color).frame(width: 32, height: 32).overlay(Circle().stroke(color == .white ? .black : .clear, lineWidth: 1))
                }.accessibilityLabel(color.accessibilityName).accessibilityIdentifier("drawing.color.\(color.rawValue)")
            } }
            HStack {
                ForEach(DrawingLineWidth.allCases, id: \.self) { width in Button { var state = draft.canvas; state.selectedLineWidth = width; store.updateDrawingCanvas(state) } label: { Circle().fill(.primary).frame(width: max(18, width.rawValue), height: max(18, width.rawValue)) }.accessibilityLabel(width.label).accessibilityIdentifier("drawing.width.\(width.rawValue)") }
                Button("drawing.eraser", systemImage: "eraser") { var state = draft.canvas; state.selectedTool = .eraser; store.updateDrawingCanvas(state) }.accessibilityIdentifier("drawing.eraser")
            }
            HStack {
                Button("drawing.undo", systemImage: "arrow.uturn.backward") { var state = draft.canvas; state.undo(); store.updateDrawingCanvas(state) }.disabled(draft.canvas.completedStrokes.isEmpty).accessibilityIdentifier("drawing.undo")
                Button("drawing.redo", systemImage: "arrow.uturn.forward") { var state = draft.canvas; state.redo(); store.updateDrawingCanvas(state) }.disabled(draft.canvas.redoStack.isEmpty).accessibilityIdentifier("drawing.redo")
                Button("drawing.clear", role: .destructive) { showClear = true }.accessibilityIdentifier("drawing.clear")
            }
            Button("drawing.done") { Task { await store.previewDrawing() } }.buttonStyle(.borderedProminent).disabled(draft.canvas.isEmpty).accessibilityIdentifier("drawing.done")
            if draft.previewPNG != nil { DrawingPreviewView(store: store, draft: draft) }
        }.alert("drawing.clear.confirm", isPresented: $showClear) { Button("common.cancel", role: .cancel) {} ; Button("drawing.clear", role: .destructive) { var state = draft.canvas; state.clear(); store.updateDrawingCanvas(state) } })
    }

    private func render(_ strokes: [DrawingStroke], context: GraphicsContext, size: CGSize) {
        for stroke in strokes { draw(stroke.points, color: stroke.tool == .eraser ? .white : stroke.color.color, width: stroke.lineWidth, context: context, size: size) }
    }
    private func renderActive(context: GraphicsContext, size: CGSize, canvas: DrawingCanvasState) { draw(active, color: canvas.selectedTool == .eraser ? .white : canvas.selectedColor.color, width: canvas.selectedLineWidth.rawValue, context: context, size: size) }
    private func draw(_ points: [DrawingPoint], color: Color, width: CGFloat, context: GraphicsContext, size: CGSize) {
        guard let first = points.first else { return }; var path = Path(); path.move(to: CGPoint(x: first.x * size.width, y: first.y * size.height)); for point in points.dropFirst() { path.addLine(to: CGPoint(x: point.x * size.width, y: point.y * size.height)) }; context.stroke(path, with: .color(color), style: StrokeStyle(lineWidth: width, lineCap: .round, lineJoin: .round))
    }
}

struct DrawingPreviewView: View {
    let store: GameSessionStore; let draft: DrawingAnswerDraft
    var body: some View { VStack(spacing: 12) {
        if let data = draft.previewPNG, let image = UIImage(data: data) { Image(uiImage: image).resizable().scaledToFit().clipShape(RoundedRectangle(cornerRadius: 14)) }
        switch store.drawingUploadPhase {
        case .uploading(let value): ProgressView(value: value).accessibilityValue("\(Int(value * 100))%")
        case .serverProcessing: ProgressView("drawing.saving")
        case .failed: Button("common.retry") { store.uploadDrawingAnswer() }.accessibilityIdentifier("drawing.retry")
        default: Button("drawing.send") { store.uploadDrawingAnswer() }.buttonStyle(.borderedProminent).accessibilityIdentifier("drawing-submit-button")
        }
    } }
}

struct DrawingWaitingView: View { let game: GameSnapshot; var body: some View { VStack(spacing: 18) { DrawingTaskHeader(game: game); Image(systemName: "checkmark.circle.fill").font(.system(size: 62)).foregroundStyle(.green); Text("drawing.sent").font(.title.bold()); Text("drawing.waiting").accessibilityIdentifier("drawing-waiting-state") } } }
struct DrawingRevealWaitingView: View { let game: GameSnapshot; var body: some View { VStack(spacing: 18) { DrawingTaskHeader(game: game); ProgressView(); Text("drawing.revealOnDisplay") } } }

struct DrawingVotingView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    @State private var selection: UUID?

    private var options: [AnonymousDrawingOption] {
        (game.drawingAnswerResults?.anonymousOptions ?? []).sorted {
            ($0.displayOrder ?? $0.revealOrder ?? 0) < ($1.displayOrder ?? $1.revealOrder ?? 0)
        }
    }

    var body: some View {
        VStack(spacing: 12) {
            DrawingTaskHeader(game: game)
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 145))]) {
                    ForEach(options) { option in
                        DrawingVoteCard(option: option, imageURL: store.mediaURL(option.thumbnailDrawingUrl), selected: option.drawingAnswerId == selectedVoteId, own: option.drawingAnswerId == store.privateGameState?.ownDrawingAnswerId) {
                            selection = option.drawingAnswerId
                            store.selectDrawingAnswerVote(option.drawingAnswerId)
                        }
                    }
                }
            }
            Button("drawing.vote") { Task { await store.submitSelectedDrawingAnswerVote() } }
                .buttonStyle(.borderedProminent)
                .disabled(selectedVoteId == nil || store.isWorking)
                .accessibilityIdentifier("drawing.vote")
        }
    }

    private var selectedVoteId: UUID? { selection ?? store.selectedDrawingAnswerVoteId }
}

private struct DrawingVoteCard: View {
    let option: AnonymousDrawingOption
    let imageURL: URL?
    let selected: Bool
    let own: Bool
    let choose: () -> Void
    var body: some View {
        Button(action: choose) {
            VStack {
                DrawingRemoteImage(url: imageURL, label: own ? "drawing.own" : "drawing.number")
                if own { Text("drawing.own") }
                if selected { Image(systemName: "checkmark.circle.fill") }
            }
        }
        .buttonStyle(.plain)
        .padding(8).contentShape(Rectangle())
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(selected ? Color.accentColor : Color.secondary, lineWidth: selected ? 4 : 1))
        .accessibilityIdentifier("drawing-voting-option-\(option.drawingAnswerId.uuidString)")
        .accessibilityLabel(own ? "drawing.own" : "drawing.number")
    }
}
struct DrawingVoteWaitingView: View { let game: GameSnapshot; var body: some View { VStack(spacing: 18) { DrawingTaskHeader(game: game); Image(systemName: "checkmark.circle.fill").font(.system(size: 62)); Text("drawing.voteSaved").accessibilityIdentifier("drawing.voteSaved") } } }
struct DrawingResultsView: View {
    let store: GameSessionStore
    let game: GameSnapshot
    var body: some View {
        VStack {
            DrawingTaskHeader(game: game)
            let options = game.drawingAnswerResults?.options ?? []
            if options.isEmpty { ContentUnavailableView("drawing.nobody", systemImage: "pencil.and.outline") }
            else {
                ScrollView {
                    ForEach(options) { option in
                        VStack(alignment: .leading) {
                            if option.isTopResult { Label("drawing.top", systemImage: "trophy.fill") }
                            DrawingRemoteImage(url: store.mediaURL(option.displayDrawingUrl), label: LocalizedStringKey(option.authorNickname))
                            Text("drawing.author \(option.authorNickname)").bold()
                            Text("\(option.voteCount) drawing.votes")
                            if !option.voters.isEmpty {
                                Text("drawing.voters")
                                ForEach(option.voters) { voter in Text("\(voter.nickname) +\(voter.pointsAwarded)") }
                            }
                        }.padding().background(.secondary.opacity(0.12), in: RoundedRectangle(cornerRadius: 14))
                    }
                }
            }
        }.accessibilityIdentifier("drawing-results-view")
    }
}
struct DrawingPrivateStateLoader: View {
    let store: GameSessionStore
    let questionId: UUID?

    var body: some View {
        VStack(spacing: 12) {
            if store.privateStateRefreshFailedQuestionId == questionId {
                Text("drawing.privateState.error")
                    .accessibilityIdentifier("drawing.private-state.error")
                Button("common.retry") {
                    Task { await store.refreshPrivateStateForActiveQuestion(questionId) }
                }
                .accessibilityIdentifier("drawing.private-state.retry")
            } else {
                ProgressView()
                    .accessibilityLabel("drawing.loading")
                    .accessibilityIdentifier("drawing.private-state.loading")
            }
        }
        .task(id: questionId) {
            guard let questionId else { return }
            guard store.privateStateRefreshFailedQuestionId != questionId else { return }
            await store.refreshPrivateStateForActiveQuestion(questionId)
        }
    }
}
struct DrawingRemoteImage: View { let url: URL?; let label: LocalizedStringKey; @State private var image: UIImage?; @State private var failed = false; var body: some View { Group { if let image { Image(uiImage: image).resizable().scaledToFit() } else if failed { ContentUnavailableView("drawing.imageUnavailable", systemImage: "photo.badge.exclamationmark") } else { ProgressView().frame(minHeight: 140) } }.background(.white).accessibilityLabel(label).task(id: url) { guard let url else { failed = true; return }; do { image = try await PhotoAnswerImageCache.shared.image(for: url) } catch { failed = true } } } }
private struct DrawingTaskHeader: View { let game: GameSnapshot; var body: some View { VStack(spacing: 6) { Text("\(game.currentRoundNumber) · \(game.currentQuestionNumber)/\(game.questionsInCurrentRound)").font(.caption); Text(game.categories?.first?.name ?? "").foregroundStyle(.secondary); Text(game.currentQuestion?.questionText.local ?? "").font(.title2.bold()).multilineTextAlignment(.center).accessibilityIdentifier("drawing-question-text") } } }
