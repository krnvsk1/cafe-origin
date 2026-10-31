import SwiftUI

// MARK: - Admin Menu View

struct AdminMenuView: View {
    @State private var dishes: [Dish] = []
    @State private var isLoading = true
    @State private var errorMessage: String = ""
    @State private var showCreateSheet = false
    @State private var editingDish: Dish? = nil
    @State private var dishToDelete: Dish? = nil

    /// Parent cards (parentId == nil)
    private var parents: [Dish] {
        dishes.filter { $0.isParentCard }.sorted { ($0.sortOrder ?? 0) < ($1.sortOrder ?? 0) }
    }

    /// Child items that don't belong to any loaded parent (shouldn't happen, but safety)
    private var orphanedChildren: [Dish] {
        let parentIds = Set(parents.map(\.id))
        return dishes.filter { $0.parentId != nil && !parentIds.contains($0.parentId ?? -1) }
    }

    var body: some View {
        VStack(spacing: 0) {
            if isLoading {
                Spacer()
                ProgressView("Загрузка меню...")
                Spacer()
            } else if dishes.isEmpty {
                Spacer()
                VStack(spacing: 12) {
                    Image(systemName: "takeoutbag.and.cup.and.straw")
                        .font(.title2)
                        .foregroundColor(.secondary)
                    Text("Меню пусто")
                        .font(.subheadline)
                        .foregroundColor(.secondary)
                    Button("Добавить блюдо") {
                        showCreateSheet = true
                    }
                    .foregroundColor(.appPrimary)
                }
                Spacer()
            } else {
                List {
                    // Parent cards with their children
                    ForEach(parents) { parent in
                        Section {
                            // Children of this parent
                            let children = (parent.children ?? []).sorted { ($0.sortOrder ?? 0) < ($1.sortOrder ?? 0) }
                            ForEach(children) { child in
                                NavigationLink {
                                    AdminCreateEditItemView(dish: child) {
                                        Task { await loadMenu() }
                                    }
                                } label: {
                                    AdminMenuItemRow(dish: child) { newValue in
                                        Task { await toggleAvailability(id: child.id, isAvailable: newValue) }
                                    }
                                }
                                .listRowInsets(EdgeInsets(top: 4, leading: 32, bottom: 4, trailing: 16))
                                .listRowSeparator(.hidden)
                                .listRowBackground(Color.clear)
                                .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                                    Button(role: .destructive) {
                                        dishToDelete = child
                                    } label: {
                                        Label("Удалить", systemImage: "trash")
                                    }

                                    Button {
                                        editingDish = child
                                    } label: {
                                        Label("Изменить", systemImage: "pencil")
                                    }
                                    .tint(.appPrimary)
                                }
                            }

                            // "Add child" button
                            Button {
                                editingDish = nil
                                showCreateChildSheet(for: parent)
                            } label: {
                                HStack(spacing: 8) {
                                    Image(systemName: "plus.circle.fill")
                                        .font(.subheadline)
                                        .foregroundColor(.appPrimary)
                                    Text("Добавить позицию")
                                        .font(.subheadline)
                                        .fontWeight(.medium)
                                        .foregroundColor(.appPrimary)
                                    Spacer()
                                }
                                .padding(.vertical, 4)
                            }
                            .listRowInsets(EdgeInsets(top: 4, leading: 32, bottom: 8, trailing: 16))
                            .listRowSeparator(.hidden)
                            .listRowBackground(Color.clear)
                        } header: {
                            // Parent card header
                            NavigationLink {
                                AdminCreateEditItemView(dish: parent) {
                                    Task { await loadMenu() }
                                }
                            } label: {
                                AdminParentCardRow(parent: parent) { newValue in
                                    Task { await toggleAvailability(id: parent.id, isAvailable: newValue) }
                                }
                            }
                            .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 4, trailing: 16))
                            .listRowSeparator(.hidden)
                            .listRowBackground(Color.clear)
                            .buttonStyle(.plain)
                        }
                    }

                    // Orphaned children (if any)
                    if !orphanedChildren.isEmpty {
                        Section {
                            ForEach(orphanedChildren) { child in
                                NavigationLink {
                                    AdminCreateEditItemView(dish: child) {
                                        Task { await loadMenu() }
                                    }
                                } label: {
                                    AdminMenuItemRow(dish: child) { newValue in
                                        Task { await toggleAvailability(id: child.id, isAvailable: newValue) }
                                    }
                                }
                                .listRowInsets(EdgeInsets(top: 4, leading: 16, bottom: 4, trailing: 16))
                                .listRowSeparator(.hidden)
                                .listRowBackground(Color.clear)
                            }
                        } header: {
                            Text("Без родителя")
                                .font(.caption)
                                .foregroundColor(.appError)
                                .padding(.horizontal, 16)
                        }
                    }
                }
                .listStyle(.plain)
            }
        }
        .navigationTitle("Управление меню")
        .navigationBarTitleDisplayMode(.inline)
        .toolbarBackground(.visible, for: .navigationBar)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                HStack(spacing: 12) {
                    Button {
                        showCreateSheet = true
                    } label: {
                        Image(systemName: "plus")
                    }

                    Button {
                        Task { await loadMenu() }
                    } label: {
                        Image(systemName: "arrow.clockwise")
                    }
                }
            }
        }
        .task { await loadMenu() }
        .refreshable { await loadMenu() }
        .sheet(isPresented: $showCreateSheet) {
            NavigationStack {
                AdminCreateEditItemView(dish: nil) {
                    showCreateSheet = false
                    Task { await loadMenu() }
                }
            }
        }
        .sheet(item: $editingDish) { dish in
            NavigationStack {
                AdminCreateEditItemView(dish: dish) {
                    editingDish = nil
                    Task { await loadMenu() }
                }
            }
        }
        .alert("Удалить блюдо?", isPresented: Binding(
            get: { dishToDelete != nil },
            set: { if !$0 { dishToDelete = nil } }
        )) {
            Button("Отмена", role: .cancel) { dishToDelete = nil }
            Button("Удалить", role: .destructive) {
                if let item = dishToDelete {
                    Task { await deleteDish(item) }
                }
            }
        } message: {
            if let item = dishToDelete {
                let childCount = item.children?.count ?? 0
                if childCount > 0 {
                    Text("«\(item.name)» и все её позиции (\(childCount) шт.) будут удалены")
                } else {
                    Text("«\(item.name)» будет удалено безвозвратно")
                }
            }
        }
    }

    // MARK: - Helpers

    @State private var childParentForCreate: Dish? = nil

    private func showCreateChildSheet(for parent: Dish) {
        childParentForCreate = parent
    }

    private func loadMenu() async {
        isLoading = true
        do {
            dishes = try await APIClient.shared.getMenu()
        } catch {
            errorMessage = error.localizedDescription
            dishes = []
        }
        isLoading = false
    }

    private func toggleAvailability(id: Int, isAvailable: Bool) async {
        do {
            try await APIClient.shared.updateDishAvailability(id: id, isAvailable: isAvailable)
            await loadMenu()
        } catch {
            await loadMenu()
        }
    }

    private func deleteDish(_ dish: Dish) async {
        do {
            try await APIClient.shared.deleteDish(id: dish.id)
            dishToDelete = nil
            await loadMenu()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

// MARK: - Admin Parent Card Row

struct AdminParentCardRow: View {
    let parent: Dish
    let onToggle: (Bool) -> Void

    var body: some View {
        HStack(spacing: 14) {
            // Thumbnail
            Group {
                if let imagePath = parent.primaryImage,
                   let url = APIClient.shared.getImageURL(imagePath) {
                    AsyncImage(url: url) { phase in
                        switch phase {
                        case .success(let image):
                            image.resizable().aspectRatio(contentMode: .fill)
                        default:
                            Color.appBackground
                        }
                    }
                } else {
                    Color.appBackground
                }
            }
            .frame(width: 56, height: 56)
            .cornerRadius(10)
            .clipped()
            .overlay(
                RoundedRectangle(cornerRadius: 10)
                    .stroke(Color.gray.opacity(0.2), lineWidth: 0.5)
            )

            // Info
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 6) {
                    Text(parent.name)
                        .font(.subheadline)
                        .fontWeight(.bold)
                        .lineLimit(1)

                    // Type badge
                    Text("Карточка")
                        .font(.caption2)
                        .fontWeight(.medium)
                        .foregroundColor(.white)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(Color.appPrimary)
                        .cornerRadius(4)

                    if parent.isPromo {
                        Text("АКЦИЯ")
                            .font(.caption2)
                            .fontWeight(.bold)
                            .foregroundColor(.white)
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(Color.appAccent)
                            .cornerRadius(4)
                    }
                }

                HStack(spacing: 8) {
                    let childCount = parent.children?.count ?? 0
                    let availableCount = parent.availableChildren?.count ?? 0
                    Text("\(availableCount)/\(childCount) поз.")
                        .font(.caption)
                        .foregroundColor(.secondary)

                    if !parent.children!.isEmpty {
                        Text("от \(Int(parent.displayPrice)) \u{20BD}")
                            .font(.caption)
                            .fontWeight(.medium)
                            .foregroundColor(.appPrimary)
                    }
                }
            }

            Spacer()

            // Availability toggle
            Toggle("", isOn: Binding(
                get: { parent.isAvailable },
                set: { onToggle($0) }
            ))
            .labelsHidden()
            .tint(.appPrimary)
        }
        .padding(12)
        .background(Color(.systemBackground))
        .cornerRadius(14)
        .shadow(color: Color.black.opacity(0.04), radius: 4, x: 0, y: 2)
        .opacity(parent.isAvailable ? 1.0 : 0.6)
    }
}

// MARK: - Admin Menu Item Row

struct AdminMenuItemRow: View {
    let dish: Dish
    let onToggle: (Bool) -> Void

    var body: some View {
        HStack(spacing: 14) {
            // Thumbnail
            Group {
                if let imagePath = dish.primaryImage,
                   let url = APIClient.shared.getImageURL(imagePath) {
                    AsyncImage(url: url) { phase in
                        switch phase {
                        case .success(let image):
                            image.resizable().aspectRatio(contentMode: .fill)
                        default:
                            Color.appBackground
                        }
                    }
                } else {
                    Color.appBackground
                }
            }
            .frame(width: 44, height: 44)
            .cornerRadius(8)
            .clipped()
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(Color.gray.opacity(0.2), lineWidth: 0.5)
            )

            // Info
            VStack(alignment: .leading, spacing: 4) {
                Text(dish.name)
                    .font(.subheadline)
                    .fontWeight(.semibold)
                    .lineLimit(1)

                HStack(spacing: 8) {
                    Text("\(Int(dish.price)) \u{20BD}")
                        .font(.caption)
                        .fontWeight(.medium)
                        .foregroundColor(.appPrimary)

                    if !dish.isAvailable {
                        Text("Недоступен")
                            .font(.caption2)
                            .foregroundColor(.appError)
                    }
                }
            }

            Spacer()

            // Availability toggle
            Toggle("", isOn: Binding(
                get: { dish.isAvailable },
                set: { onToggle($0) }
            ))
            .labelsHidden()
            .tint(.appPrimary)
        }
        .padding(10)
        .background(Color(.systemBackground))
        .cornerRadius(12)
        .shadow(color: Color.black.opacity(0.03), radius: 3, x: 0, y: 1)
        .opacity(dish.isAvailable ? 1.0 : 0.6)
    }
}
